using Arb.Core.Application.Abstractions.Persistence;
using Arb.Core.Contracts.Events;
using Arb.Core.Executor.Worker.Options;
using Arb.Core.Infrastructure.External.Polymarket;
using Arb.Core.Infrastructure.Redis;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace Arb.Core.Executor.Worker.HostedServices
{
    public class PolymarketExitMonitorService : BackgroundService
    {
        // Motivos de fechamento — gravados em exit_reason para auditoria completa
        private const string ExitConverged = "CONVERGED";
        private const string ExitKickoffFallback = "KICKOFF_FALLBACK";
        private const string ExitKickoffNoPrice = "KICKOFF_NO_PRICE";
        private const string ExitExpiredNoClose = "EXPIRED_NO_CLOSE";

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly PolymarketClobPriceClient _clobPriceClient;
        private readonly SettlementOptions _settlementOptions;
        private readonly StreamsOptions _streams;
        private readonly ILogger<PolymarketExitMonitorService> _logger;

        public PolymarketExitMonitorService(
            IServiceScopeFactory scopeFactory,
            PolymarketClobPriceClient clobPriceClient,
            IOptions<SettlementOptions> settlementOptions,
            IOptions<StreamsOptions> streamsOptions,
            ILogger<PolymarketExitMonitorService> logger)
        {
            _scopeFactory = scopeFactory;
            _clobPriceClient = clobPriceClient;
            _settlementOptions = settlementOptions.Value;
            _streams = streamsOptions.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "PolymarketExitMonitor started. " +
                "Interval={Interval}s KickoffWindow={KickoffWindow}min MaxPriceAge={MaxAge}min",
                _settlementOptions.ExitMonitorIntervalSeconds,
                _settlementOptions.MinutesBeforeKickoffToClose,
                _settlementOptions.MaxPriceAgeMinutes);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessOpenPositionsAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "PolymarketExitMonitor cycle failed");
                }

                await Task.Delay(
                    TimeSpan.FromSeconds(_settlementOptions.ExitMonitorIntervalSeconds),
                    stoppingToken);
            }

            _logger.LogInformation("PolymarketExitMonitor stopped");
        }

        private async Task ProcessOpenPositionsAsync(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();

            var positionRepo = scope.ServiceProvider
                .GetRequiredService<IPositionRepository>();

            var positions = await positionRepo.ListOpenPolymarketPositionsAsync(ct);

            if (positions.Count == 0)
                return;

            _logger.LogDebug(
                "PolymarketExitMonitor scanning {Count} open position(s)",
                positions.Count);

            var tokenIds = positions
                .Where(p => !string.IsNullOrWhiteSpace(p.TargetTokenId))
                .Select(p => p.TargetTokenId!)
                .Distinct()
                .ToList();

            IReadOnlyDictionary<string, decimal> midPrices =
                new Dictionary<string, decimal>();

            if (tokenIds.Count > 0)
            {
                var midPriceMap = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

                foreach (var tokenId in tokenIds)
                {
                    var midpoint = await _clobPriceClient.GetMidpointAsync(tokenId, ct);
                    if (midpoint.HasValue)
                    {
                        midPriceMap[tokenId] = midpoint.Value;
                    }
                }

                midPrices = midPriceMap;

                var missingTokenIds = tokenIds
                    .Where(x => !midPrices.ContainsKey(x))
                    .Take(10)
                    .ToArray();

                _logger.LogInformation(
                    "CLOB midpoints fetched. Requested={Requested} Returned={Returned} MissingSample={MissingSample}",
                    tokenIds.Count,
                    midPrices.Count,
                    missingTokenIds.Length == 0 ? "none" : string.Join(",", missingTokenIds));
            }

            var utcNow = DateTime.UtcNow;

            foreach (var position in positions)
            {
                await ProcessPositionAsync(
                    position, midPrices, utcNow, positionRepo, scope, ct);
            }
        }

        private async Task ProcessPositionAsync(
            OpenPositionForSettlement position,
            IReadOnlyDictionary<string, decimal> midPrices,
            DateTime utcNow,
            IPositionRepository positionRepo,
            IServiceScope scope,
            CancellationToken ct)
        {
            var timeToKickoff = position.CommenceTime - utcNow;
            var kickoffWindowReached = timeToKickoff <=
                TimeSpan.FromMinutes(_settlementOptions.MinutesBeforeKickoffToClose);
            var kickoffPassed = utcNow > position.CommenceTime;

            // Tenta obter o preço atual da CLOB
            decimal? currentMidPrice = null;

            if (!string.IsNullOrWhiteSpace(position.TargetTokenId) &&
                midPrices.TryGetValue(position.TargetTokenId, out var fetchedMid))
            {
                currentMidPrice = fetchedMid;

                // Atualiza o last_known_mid_price no banco — proteção para o fallback de kickoff
                await positionRepo.UpdateLastKnownMidPriceAsync(
                    position.Id,
                    (double)fetchedMid,
                    utcNow,
                    ct);
            }

            // ── Cenário 1: Jogo já começou com posição ainda aberta ─────────────────
            // Situação crítica — o monitor falhou em fechar antes do kickoff
            if (kickoffPassed)
            {
                _logger.LogError(
                    "CRITICAL: Position expired without exit. " +
                    "positionId={PositionId} team={Team} conditionId={ConditionId} " +
                    "commenceTime={CommenceTime} utcNow={UtcNow}",
                    position.Id,
                    position.ObservedTeam,
                    position.PolymarketConditionId,
                    position.CommenceTime.ToString("O"),
                    utcNow.ToString("O"));

                await ClosePositionAsync(
                    position: position,
                    closePrice: currentMidPrice ?? (decimal?)position.LastKnownMidPrice,
                    exitReason: ExitExpiredNoClose,
                    utcNow: utcNow,
                    positionRepo: positionRepo,
                    scope: scope,
                    ct: ct);

                return;
            }

            // ── Cenário 2: Convergência atingida ────────────────────────────────────
            // O preço da Polymarket chegou ao alvo asiático — fecha com lucro
            if (currentMidPrice.HasValue &&
                position.TargetProbability.HasValue &&
                currentMidPrice.Value >= (decimal)position.TargetProbability.Value)
            {
                _logger.LogInformation(
                    "Convergence reached. positionId={PositionId} team={Team} " +
                    "mid={Mid:F4} target={Target:F4} " +
                    "entry={Entry} timeToKickoff={TimeToKickoff}",
                    position.Id,
                    position.ObservedTeam,
                    currentMidPrice.Value,
                    position.TargetProbability.Value,
                    position.PolymarketEntryPrice?.ToString("F4", CultureInfo.InvariantCulture) ?? "N/A",
                    timeToKickoff.ToString(@"hh\:mm\:ss"));

                await ClosePositionAsync(
                    position: position,
                    closePrice: currentMidPrice,
                    exitReason: ExitConverged,
                    utcNow: utcNow,
                    positionRepo: positionRepo,
                    scope: scope,
                    ct: ct);

                return;
            }

            // ── Cenário 3: Janela de kickoff atingida ───────────────────────────────
            // Independente de convergência, força o fechamento antes do jogo
            if (kickoffWindowReached)
            {
                // Sub-cenário 3a: temos preço atual confiável
                if (currentMidPrice.HasValue)
                {
                    _logger.LogInformation(
                        "Kickoff window reached — closing with current price. " +
                        "positionId={PositionId} team={Team} " +
                        "mid={Mid:F4} target={Target} " +
                        "timeToKickoff={TimeToKickoff}",
                        position.Id,
                        position.ObservedTeam,
                        currentMidPrice.Value,
                        position.TargetProbability?.ToString("F4") ?? "N/A",
                        timeToKickoff.ToString(@"mm\:ss"));

                    await ClosePositionAsync(
                        position: position,
                        closePrice: currentMidPrice,
                        exitReason: ExitKickoffFallback,
                        utcNow: utcNow,
                        positionRepo: positionRepo,
                        scope: scope,
                        ct: ct);

                    return;
                }

                // Sub-cenário 3b: CLOB indisponível — usa last_known_mid_price se recente
                if (position.LastKnownMidPrice.HasValue &&
                    position.LastPriceCheckedAt.HasValue)
                {
                    var priceAge = utcNow - position.LastPriceCheckedAt.Value;
                    var maxAge = TimeSpan.FromMinutes(_settlementOptions.MaxPriceAgeMinutes);

                    if (priceAge <= maxAge)
                    {
                        _logger.LogWarning(
                            "Kickoff window reached — CLOB unavailable, using last known price. " +
                            "positionId={PositionId} team={Team} " +
                            "lastMid={LastMid:F4} priceAge={PriceAge}",
                            position.Id,
                            position.ObservedTeam,
                            position.LastKnownMidPrice.Value,
                            priceAge.ToString(@"mm\:ss"));

                        await ClosePositionAsync(
                            position: position,
                            closePrice: (decimal)position.LastKnownMidPrice.Value,
                            exitReason: ExitKickoffFallback,
                            utcNow: utcNow,
                            positionRepo: positionRepo,
                            scope: scope,
                            ct: ct);

                        return;
                    }
                }

                // Sub-cenário 3c: sem preço confiável — fecha sem PnL, loga alerta
                _logger.LogError(
                    "Kickoff window reached — no reliable price available. " +
                    "positionId={PositionId} team={Team} " +
                    "lastChecked={LastChecked} timeToKickoff={TimeToKickoff}",
                    position.Id,
                    position.ObservedTeam,
                    position.LastPriceCheckedAt?.ToString("O") ?? "never",
                    timeToKickoff.ToString(@"mm\:ss"));

                await ClosePositionAsync(
                    position: position,
                    closePrice: null,
                    exitReason: ExitKickoffNoPrice,
                    utcNow: utcNow,
                    positionRepo: positionRepo,
                    scope: scope,
                    ct: ct);

                return;
            }

            // ── Nenhuma condição de saída atingida ──────────────────────────────────
            // Loga o estado atual para acompanhamento
            _logger.LogDebug(
                "Position monitoring. positionId={PositionId} team={Team} " +
                "mid={Mid} target={Target} " +
                "entry={Entry} timeToKickoff={TimeToKickoff}",
                position.Id,
                position.ObservedTeam,
                currentMidPrice.HasValue
                    ? currentMidPrice.Value.ToString("F4", CultureInfo.InvariantCulture)
                    : "N/A",
                position.TargetProbability?.ToString("F4") ?? "N/A",
                position.PolymarketEntryPrice?.ToString("F4", CultureInfo.InvariantCulture) ?? "N/A",
                timeToKickoff.ToString(@"hh\:mm\:ss"));
        }

        private async Task ClosePositionAsync(
            OpenPositionForSettlement position,
            decimal? closePrice,
            string exitReason,
            DateTime utcNow,
            IPositionRepository positionRepo,
            IServiceScope scope,
            CancellationToken ct)
        {
            // PnL calculado como diferença de preço em probabilidade * tokens comprados
            // tokens = stake / entryPrice
            // PnL = (closePrice - entryPrice) * tokens
            //     = (closePrice - entryPrice) * (stake / entryPrice)
            double? pnl = null;
            var entryPrice = position.PolymarketEntryPrice ?? position.EntryPrice;

            if (closePrice.HasValue && entryPrice > 0)
            {
                var tokens = position.Stake / entryPrice;
                pnl = ((double)closePrice.Value - entryPrice) * tokens;
                pnl = Math.Round(pnl.Value, 4, MidpointRounding.AwayFromZero);
            }

            await positionRepo.CloseAsync(
                positionId: position.Id,
                pnl: pnl ?? 0,
                closedAt: utcNow,
                closePrice: closePrice.HasValue ? (double)closePrice.Value : null,
                exitReason: exitReason,
                ct: ct);

            // Atualiza o balance do portfolio
            var portfolioRepo = scope.ServiceProvider
                .GetRequiredService<IPortfolioRepository>();

            var portfolio = await portfolioRepo.GetAsync(ct);
            if (portfolio is not null)
            {
                // Devolve o stake + PnL ao portfolio
                var newBalance = portfolio.CurrentBalance + position.Stake + (pnl ?? 0);
                await portfolioRepo.UpdateBalanceAsync(newBalance, utcNow, ct);
            }

            // Publica execution report
            var reportRepo = scope.ServiceProvider
                .GetRequiredService<IExecutionReportRepository>();

            var statusLabel = exitReason switch
            {
                ExitConverged => "SETTLED_CONVERGED",
                ExitKickoffFallback => "SETTLED_KICKOFF",
                ExitKickoffNoPrice => "SETTLED_NO_PRICE",
                ExitExpiredNoClose => "SETTLED_EXPIRED",
                _ => "SETTLED"
            };

            var report = new ExecutionReportV1(
                SchemaVersion: "1.0.0",
                ReportId: Guid.NewGuid().ToString("N"),
                IntentId: position.Id.ToString("N"),
                CorrelationId: $"{position.EventKey}|{position.SelectionKey}|{exitReason}",
                Ts: utcNow,
                Status: statusLabel,
                FilledPrice: closePrice.HasValue ? (double)closePrice.Value : null,
                FilledUsd: position.Stake,
                TxHash: null,
                Error: exitReason == ExitKickoffNoPrice || exitReason == ExitExpiredNoClose
                    ? "No reliable price available at close time"
                    : null);

            await reportRepo.InsertAsync(report, ct);

            _logger.LogInformation(
                "Position closed. positionId={PositionId} team={Team} " +
                "exitReason={ExitReason} entry={Entry:F4} close={Close} " +
                "pnl={Pnl} stake={Stake}",
                position.Id,
                position.ObservedTeam,
                exitReason,
                entryPrice,
                closePrice.HasValue
                    ? closePrice.Value.ToString("F4", CultureInfo.InvariantCulture)
                    : "N/A",
                pnl.HasValue
                    ? pnl.Value.ToString("F4", CultureInfo.InvariantCulture)
                    : "N/A",
                position.Stake);
        }
    }
}


