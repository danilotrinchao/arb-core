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
        private const string ExitConverged = "CONVERGED";
        private const string ExitKickoffFallback = "KICKOFF_FALLBACK";
        private const string ExitKickoffNoPrice = "KICKOFF_NO_PRICE";
        private const string ExitExpiredNoClose = "EXPIRED_NO_CLOSE";

        // Guard 2:
        // só consideramos convergência válida se houver melhora mínima real
        // sobre o preço de entrada na Polymarket.
        private const double MinImprovementOverEntryToConverge = 0.02d;

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
            var kickoffWindowReached = timeToKickoff <= TimeSpan.FromMinutes(_settlementOptions.MinutesBeforeKickoffToClose);
            var kickoffPassed = utcNow > position.CommenceTime;

            decimal? currentMidPrice = null;
            if (!string.IsNullOrWhiteSpace(position.TargetTokenId) &&
                midPrices.TryGetValue(position.TargetTokenId, out var fetchedMid))
            {
                currentMidPrice = fetchedMid;
                await positionRepo.UpdateLastKnownMidPriceAsync(
                    position.Id,
                    (double)fetchedMid,
                    utcNow,
                    ct);
            }

            var comparableTargetProbability = GetComparableTargetProbability(position);

            // Guard 2: só fecha por convergência se houve melhora mínima real
            // acima do preço de entrada na Polymarket.
            var entryForConvergence = position.PolymarketEntryPrice ?? position.EntryPrice;
            double? minConvergedExitPrice = null;

            if (entryForConvergence > 0)
            {
                minConvergedExitPrice = Math.Round(
                    entryForConvergence + MinImprovementOverEntryToConverge,
                    4,
                    MidpointRounding.AwayFromZero);
            }

            var hasImprovedSinceEntry =
                currentMidPrice.HasValue &&
                minConvergedExitPrice.HasValue &&
                (double)currentMidPrice.Value >= minConvergedExitPrice.Value;

            if (!kickoffPassed &&
                currentMidPrice.HasValue &&
                comparableTargetProbability.HasValue &&
                currentMidPrice.Value >= (decimal)comparableTargetProbability.Value &&
                hasImprovedSinceEntry)
            {
                _logger.LogInformation(
                    "Convergence reached. positionId={PositionId} team={Team} " +
                    "mid={Mid:F4} comparableTarget={ComparableTarget:F4} requiredExit={RequiredExit:F4} rawTarget={RawTarget:F4} " +
                    "targetSide={TargetSide} entry={Entry} timeToKickoff={TimeToKickoff}",
                    position.Id,
                    position.ObservedTeam,
                    currentMidPrice.Value,
                    comparableTargetProbability.Value,
                    minConvergedExitPrice.Value,
                    position.TargetProbability ?? 0,
                    position.TargetSide,
                    position.PolymarketEntryPrice?.ToString("F4", CultureInfo.InvariantCulture) ?? "N/A",
                    timeToKickoff.ToString(@"hh\:mm\:ss"));

                await ClosePositionAsync(
                    position: position,
                    closePrice: currentMidPrice,
                    exitReason: ExitConverged,
                    utcNow: utcNow,
                    positionRepo: positionRepo,
                    scope: scope,
                    hadMissingMidpointAtClose: false,
                    usedLastKnownMidPriceFallback: false,
                    ct: ct);
                return;
            }

            if (kickoffWindowReached)
            {
                if (currentMidPrice.HasValue)
                {
                    _logger.LogInformation(
                        "Kickoff window reached — closing with current price. " +
                        "positionId={PositionId} team={Team} " +
                        "mid={Mid:F4} comparableTarget={ComparableTarget} requiredExit={RequiredExit} rawTarget={RawTarget} " +
                        "targetSide={TargetSide} timeToKickoff={TimeToKickoff}",
                        position.Id,
                        position.ObservedTeam,
                        currentMidPrice.Value,
                        comparableTargetProbability.HasValue
                            ? comparableTargetProbability.Value.ToString("F4", CultureInfo.InvariantCulture)
                            : "N/A",
                        minConvergedExitPrice.HasValue
                            ? minConvergedExitPrice.Value.ToString("F4", CultureInfo.InvariantCulture)
                            : "N/A",
                        position.TargetProbability?.ToString("F4", CultureInfo.InvariantCulture) ?? "N/A",
                        position.TargetSide,
                        timeToKickoff.ToString(@"mm\:ss"));

                    await ClosePositionAsync(
                        position: position,
                        closePrice: currentMidPrice,
                        exitReason: ExitKickoffFallback,
                        utcNow: utcNow,
                        positionRepo: positionRepo,
                        scope: scope,
                        hadMissingMidpointAtClose: false,
                        usedLastKnownMidPriceFallback: false,
                        ct: ct);
                    return;
                }

                if (position.LastKnownMidPrice.HasValue && position.LastPriceCheckedAt.HasValue)
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
                            hadMissingMidpointAtClose: true,
                            usedLastKnownMidPriceFallback: true,
                            ct: ct);
                        return;
                    }
                }

                if (kickoffPassed)
                {
                    _logger.LogError(
                        "CRITICAL: Position expired without reliable close price. " +
                        "positionId={PositionId} team={Team} conditionId={ConditionId} " +
                        "commenceTime={CommenceTime} utcNow={UtcNow}",
                        position.Id,
                        position.ObservedTeam,
                        position.PolymarketConditionId,
                        position.CommenceTime.ToString("O"),
                        utcNow.ToString("O"));

                    await ClosePositionAsync(
                        position: position,
                        closePrice: null,
                        exitReason: ExitExpiredNoClose,
                        utcNow: utcNow,
                        positionRepo: positionRepo,
                        scope: scope,
                        hadMissingMidpointAtClose: true,
                        usedLastKnownMidPriceFallback: false,
                        ct: ct);
                    return;
                }

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
                    hadMissingMidpointAtClose: true,
                    usedLastKnownMidPriceFallback: false,
                    ct: ct);
                return;
            }

            _logger.LogDebug(
                "Position monitoring. positionId={PositionId} team={Team} " +
                "mid={Mid} comparableTarget={ComparableTarget} requiredExit={RequiredExit} rawTarget={RawTarget} " +
                "targetSide={TargetSide} entry={Entry} timeToKickoff={TimeToKickoff}",
                position.Id,
                position.ObservedTeam,
                currentMidPrice.HasValue
                    ? currentMidPrice.Value.ToString("F4", CultureInfo.InvariantCulture)
                    : "N/A",
                comparableTargetProbability.HasValue
                    ? comparableTargetProbability.Value.ToString("F4", CultureInfo.InvariantCulture)
                    : "N/A",
                minConvergedExitPrice.HasValue
                    ? minConvergedExitPrice.Value.ToString("F4", CultureInfo.InvariantCulture)
                    : "N/A",
                position.TargetProbability?.ToString("F4", CultureInfo.InvariantCulture) ?? "N/A",
                position.TargetSide,
                position.PolymarketEntryPrice?.ToString("F4", CultureInfo.InvariantCulture) ?? "N/A",
                timeToKickoff.ToString(@"hh\:mm\:ss"));
        }

        private static double? GetComparableTargetProbability(OpenPositionForSettlement position)
        {
            if (!position.TargetProbability.HasValue) return null;

            var rawTarget = position.TargetProbability.Value;
            if (rawTarget <= 0) return 0;
            if (rawTarget >= 1) return 1;

            var targetSide = position.TargetSide ?? string.Empty;

            if (string.Equals(targetSide, "YES", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(targetSide, "SIDE_A", StringComparison.OrdinalIgnoreCase))
            {
                return Math.Round(rawTarget, 4, MidpointRounding.AwayFromZero);
            }

            if (string.Equals(targetSide, "NO", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(targetSide, "SIDE_B", StringComparison.OrdinalIgnoreCase))
            {
                return Math.Round(1d - rawTarget, 4, MidpointRounding.AwayFromZero);
            }

            return Math.Round(rawTarget, 4, MidpointRounding.AwayFromZero);
        }

        private async Task ClosePositionAsync(
            OpenPositionForSettlement position,
            decimal? closePrice,
            string exitReason,
            DateTime utcNow,
            IPositionRepository positionRepo,
            IServiceScope scope,
            bool hadMissingMidpointAtClose,
            bool usedLastKnownMidPriceFallback,
            CancellationToken ct)
        {
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

            var portfolioRepo = scope.ServiceProvider
                .GetRequiredService<IPortfolioRepository>();

            var portfolio = await portfolioRepo.GetAsync(ct);
            if (portfolio is not null)
            {
                var newBalance = portfolio.CurrentBalance + position.Stake + (pnl ?? 0);
                await portfolioRepo.UpdateBalanceAsync(newBalance, utcNow, ct);
            }

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
                IntentId: position.IntentId.ToString("N"),
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

            var analyticsRepo = scope.ServiceProvider
                .GetRequiredService<IPositionAnalyticsRepository>();

            var analytics = new PositionClosureAnalytics(
                PositionId: position.Id,
                IntentId: position.IntentId,
                SportKey: position.SportKey,
                EventKey: position.EventKey,
                MarketType: position.MarketType,
                SelectionKey: position.SelectionKey,
                TargetSide: position.TargetSide,
                ObservedTeam: position.ObservedTeam,
                PolymarketConditionId: position.PolymarketConditionId,
                TargetTokenId: position.TargetTokenId,
                CommenceTime: position.CommenceTime,
                OpenedAt: position.CreatedAt,
                ClosedAt: utcNow,
                Stake: position.Stake,
                EntryPrice: position.EntryPrice,
                PolymarketEntryPrice: position.PolymarketEntryPrice,
                ClosePrice: closePrice.HasValue ? (double)closePrice.Value : null,
                PnL: pnl,
                ExitReason: exitReason,
                TargetProbability: position.TargetProbability,
                LastKnownMidPrice: position.LastKnownMidPrice,
                LastPriceCheckedAt: position.LastPriceCheckedAt,
                TimeToKickoffAtEntrySeconds: (position.CommenceTime - position.CreatedAt).TotalSeconds,
                TimeToKickoffAtCloseSeconds: (position.CommenceTime - utcNow).TotalSeconds,
                HadMissingMidpointAtClose: hadMissingMidpointAtClose,
                UsedLastKnownMidPriceFallback: usedLastKnownMidPriceFallback
            );

            await analyticsRepo.InsertClosureAnalyticsAsync(analytics, ct);

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