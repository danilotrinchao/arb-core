using Arb.Core.Application.Abstractions.Messaging;
using Arb.Core.Application.Abstractions.Persistence;
using Arb.Core.Application.Abstractions.Settlement;
using Arb.Core.Contracts.Common.PolimarketSignals;
using Arb.Core.Contracts.Events;
using Arb.Core.Executor.Worker.Options;
using Arb.Core.Infrastructure.External.Polymarket;
using Arb.Core.Infrastructure.Redis;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Globalization;
using System.Text.Json;

namespace Arb.Core.Executor.Worker
{
    public class Worker : BackgroundService
    {
        private const string PolymarketGroupName = "executor-polymarket";
        private const string PolymarketConsumerName = "executor-polymarket-1";
        private const string PolymarketMarketType = "TEAM_TO_WIN_YES_NO";
        private const double MinHeadroomToTargetToOpen = 0.012d;

        private readonly ILogger<Worker> _logger;
        private readonly IStreamConsumer _consumer;
        private readonly IStreamPublisher _publisher;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly PolymarketClobPriceClient _clobPriceClient;
        private readonly StreamsOptions _streams;
        private readonly ExecutorOptions _executorOptions;
        private readonly SettlementOptions _settlement;
        private readonly RiskOptions _risk;

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public Worker(
            ILogger<Worker> logger,
            IStreamConsumer consumer,
            IStreamPublisher publisher,
            IServiceScopeFactory scopeFactory,
            PolymarketClobPriceClient clobPriceClient,
            IOptions<StreamsOptions> streamsOptions,
            IOptions<ExecutorOptions> executorOptions,
            IOptions<SettlementOptions> settlementOptions,
            IOptions<RiskOptions> riskOptions)
        {
            _logger = logger;
            _consumer = consumer;
            _publisher = publisher;
            _scopeFactory = scopeFactory;
            _clobPriceClient = clobPriceClient;
            _streams = streamsOptions.Value;
            _executorOptions = executorOptions.Value;
            _settlement = settlementOptions.Value;
            _risk = riskOptions.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await _consumer.EnsureConsumerGroupAsync(
                _streams.PolymarketOrderIntents,
                PolymarketGroupName,
                stoppingToken);

            using (var scope = _scopeFactory.CreateScope())
            {
                var portfolioRepo = scope.ServiceProvider
                    .GetRequiredService<IPortfolioRepository>();

                await portfolioRepo.EnsureInitializedAsync(
                    _executorOptions.InitialBalance,
                    stoppingToken);
            }

            _logger.LogInformation(
                "Executor started. PolymarketConsuming={Stream} FixedStake={Stake}",
                _streams.PolymarketOrderIntents,
                _risk.PolymarketFixedStakeUsd);

            await RunPolymarketExecutionLoopAsync(stoppingToken);
        }

        private async Task RunPolymarketExecutionLoopAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                IReadOnlyList<StreamMessage> messages;

                try
                {
                    messages = await _consumer.ReadGroupAsync(
                        _streams.PolymarketOrderIntents,
                        PolymarketGroupName,
                        PolymarketConsumerName,
                        count: 20,
                        block: TimeSpan.FromSeconds(2),
                        stoppingToken);
                }
                catch (RedisTimeoutException ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Redis timeout reading Polymarket stream. Retrying...");
                    await Task.Delay(1000, stoppingToken);
                    continue;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Unexpected error reading Polymarket stream");
                    await Task.Delay(1000, stoppingToken);
                    continue;
                }

                if (messages.Count == 0)
                {
                    await Task.Delay(1000, stoppingToken);
                    continue;
                }

                foreach (var msg in messages)
                {
                    try
                    {
                        if (!msg.Fields.TryGetValue("payload", out var payload) ||
                            string.IsNullOrWhiteSpace(payload))
                        {
                            await AckWithRetryAsync(
                                _streams.PolymarketOrderIntents,
                                PolymarketGroupName,
                                msg.Id,
                                stoppingToken);
                            continue;
                        }

                        var intent = JsonSerializer.Deserialize<PolymarketOrderIntentV1>(
                            payload,
                            JsonOpts);

                        if (intent is null)
                        {
                            await AckWithRetryAsync(
                                _streams.PolymarketOrderIntents,
                                PolymarketGroupName,
                                msg.Id,
                                stoppingToken);
                            continue;
                        }

                        using var scope = _scopeFactory.CreateScope();

                        var portfolioRepo = scope.ServiceProvider
                            .GetRequiredService<IPortfolioRepository>();
                        var positionRepo = scope.ServiceProvider
                            .GetRequiredService<IPositionRepository>();
                        var reportRepo = scope.ServiceProvider
                            .GetRequiredService<IExecutionReportRepository>();
                        var orderIntentRepo = scope.ServiceProvider
                            .GetRequiredService<IOrderIntentRepository>();
                        var rejectionRepo = scope.ServiceProvider
                            .GetRequiredService<IOrderIntentRejectionRepository>();

                        await PersistOrderIntentAsync(intent, orderIntentRepo, stoppingToken);

                        var portfolio = await portfolioRepo.GetAsync(stoppingToken);
                        if (portfolio is null)
                        {
                            await portfolioRepo.EnsureInitializedAsync(
                                _executorOptions.InitialBalance,
                                stoppingToken);

                            portfolio = await portfolioRepo.GetAsync(stoppingToken);
                        }

                        var openPolymarketPositions =
                            await positionRepo.CountOpenPolymarketAsync(stoppingToken);

                        if (openPolymarketPositions >= _risk.MaxPolymarketOpenPositions)
                        {
                            await PersistRejectionAsync(
                                rejectionRepo,
                                intent,
                                reason: "MAX_POLYMARKET_OPEN_POSITIONS",
                                entryMid: null,
                                comparableTarget: null,
                                headroomToTarget: null,
                                timeToKickoffSeconds: null,
                                payload,
                                stoppingToken);

                            _logger.LogInformation(
                                "Polymarket intent rejected. Reason=MAX_POLYMARKET_OPEN_POSITIONS intentId={IntentId} open={Open} max={Max}",
                                intent.IntentId,
                                openPolymarketPositions,
                                _risk.MaxPolymarketOpenPositions);

                            await _consumer.AckAsync(
                                _streams.PolymarketOrderIntents,
                                PolymarketGroupName,
                                msg.Id,
                                stoppingToken);
                            continue;
                        }

                        var effectiveStake = _risk.PolymarketFixedStakeUsd;

                        if (effectiveStake > portfolio!.CurrentBalance)
                        {
                            await PersistRejectionAsync(
                                rejectionRepo,
                                intent,
                                reason: "INSUFFICIENT_BALANCE",
                                entryMid: null,
                                comparableTarget: null,
                                headroomToTarget: null,
                                timeToKickoffSeconds: null,
                                payload,
                                stoppingToken);

                            _logger.LogInformation(
                                "Polymarket intent rejected. Reason=INSUFFICIENT_BALANCE intentId={IntentId} stake={Stake} balance={Balance}",
                                intent.IntentId,
                                effectiveStake,
                                portfolio.CurrentBalance);

                            await _consumer.AckAsync(
                                _streams.PolymarketOrderIntents,
                                PolymarketGroupName,
                                msg.Id,
                                stoppingToken);
                            continue;
                        }

                        var utcNow = DateTime.UtcNow;
                        var commenceTime = ResolveCommenceTime(intent, utcNow);

                        var timeToKickoff = commenceTime - utcNow;
                        var kickoffWindow = TimeSpan.FromMinutes(
                            _settlement.MinutesBeforeKickoffToClose);

                        if (timeToKickoff <= kickoffWindow)
                        {
                            await PersistRejectionAsync(
                                rejectionRepo,
                                intent,
                                reason: "INSIDE_KICKOFF_WINDOW",
                                entryMid: null,
                                comparableTarget: null,
                                headroomToTarget: null,
                                timeToKickoffSeconds: timeToKickoff.TotalSeconds,
                                rawPayload: payload,
                                ct: stoppingToken);

                            _logger.LogInformation(
                                "Polymarket intent rejected. Reason=INSIDE_KICKOFF_WINDOW intentId={IntentId} team={Team} conditionId={ConditionId} commenceTime={CommenceTime} matchedGammaStartTime={MatchedGammaStartTime} timeToKickoff={TimeToKickoff} kickoffWindowMinutes={KickoffWindowMinutes}",
                                intent.IntentId,
                                intent.ObservedTeam,
                                intent.PolymarketConditionId,
                                intent.CommenceTime ?? "null",
                                intent.MatchedGammaStartTime ?? "null",
                                timeToKickoff.ToString(),
                                _settlement.MinutesBeforeKickoffToClose);

                            await _consumer.AckAsync(
                                _streams.PolymarketOrderIntents,
                                PolymarketGroupName,
                                msg.Id,
                                stoppingToken);
                            continue;
                        }

                        double? polymarketEntryPrice = null;

                        if (!string.IsNullOrWhiteSpace(intent.TargetTokenId))
                        {
                            var midpoint = await _clobPriceClient.GetMidpointAsync(
                                intent.TargetTokenId,
                                stoppingToken);

                            if (midpoint.HasValue)
                            {
                                polymarketEntryPrice = (double)midpoint.Value;

                                _logger.LogInformation(
                                    "Polymarket entry price fetched. TokenId={TokenId} MidPrice={MidPrice}",
                                    intent.TargetTokenId,
                                    polymarketEntryPrice);
                            }
                            else
                            {
                                _logger.LogWarning(
                                    "Could not fetch Polymarket entry price for TokenId={TokenId}. Position will be opened with null entry price.",
                                    intent.TargetTokenId);
                            }
                        }

                        var comparableTargetProbability = GetComparableTargetProbability(intent);

                        if (polymarketEntryPrice.HasValue &&
                            comparableTargetProbability.HasValue &&
                            polymarketEntryPrice.Value >= comparableTargetProbability.Value)
                        {
                            await PersistRejectionAsync(
                                rejectionRepo,
                                intent,
                                reason: "ENTRY_ALREADY_AT_OR_ABOVE_TARGET",
                                entryMid: polymarketEntryPrice,
                                comparableTarget: comparableTargetProbability,
                                headroomToTarget: comparableTargetProbability - polymarketEntryPrice,
                                timeToKickoffSeconds: timeToKickoff.TotalSeconds,
                                rawPayload: payload,
                                ct: stoppingToken);

                            _logger.LogInformation(
                                "Polymarket intent rejected. Reason=ENTRY_ALREADY_AT_OR_ABOVE_TARGET intentId={IntentId} team={Team} conditionId={ConditionId} targetSide={TargetSide} entryMid={EntryMid:F4} comparableTarget={ComparableTarget:F4} rawTarget={RawTarget:F4}",
                                intent.IntentId,
                                intent.ObservedTeam,
                                intent.PolymarketConditionId,
                                intent.TargetSide,
                                polymarketEntryPrice.Value,
                                comparableTargetProbability.Value,
                                intent.TargetProbability);

                            await _consumer.AckAsync(
                                _streams.PolymarketOrderIntents,
                                PolymarketGroupName,
                                msg.Id,
                                stoppingToken);
                            continue;
                        }

                        if (polymarketEntryPrice.HasValue &&
                            comparableTargetProbability.HasValue)
                        {
                            var headroomToTarget =
                                comparableTargetProbability.Value - polymarketEntryPrice.Value;

                            if (headroomToTarget < MinHeadroomToTargetToOpen)
                            {
                                await PersistRejectionAsync(
                                    rejectionRepo,
                                    intent,
                                    reason: "ENTRY_HEADROOM_BELOW_MINIMUM",
                                    entryMid: polymarketEntryPrice,
                                    comparableTarget: comparableTargetProbability,
                                    headroomToTarget: headroomToTarget,
                                    timeToKickoffSeconds: timeToKickoff.TotalSeconds,
                                    rawPayload: payload,
                                    ct: stoppingToken);

                                _logger.LogInformation(
                                    "Polymarket intent rejected. Reason=ENTRY_HEADROOM_BELOW_MINIMUM intentId={IntentId} team={Team} conditionId={ConditionId} targetSide={TargetSide} entryMid={EntryMid:F4} comparableTarget={ComparableTarget:F4} headroom={Headroom:F4} minHeadroom={MinHeadroom:F4} rawTarget={RawTarget:F4}",
                                    intent.IntentId,
                                    intent.ObservedTeam,
                                    intent.PolymarketConditionId,
                                    intent.TargetSide,
                                    polymarketEntryPrice.Value,
                                    comparableTargetProbability.Value,
                                    headroomToTarget,
                                    MinHeadroomToTargetToOpen,
                                    intent.TargetProbability);

                                await _consumer.AckAsync(
                                    _streams.PolymarketOrderIntents,
                                    PolymarketGroupName,
                                    msg.Id,
                                    stoppingToken);
                                continue;
                            }
                        }

                        var positionId = await positionRepo.CreateOpenAsync(
                            new PositionOpen(
                                IntentId: intent.IntentId,
                                SportKey: intent.SportKey ?? "soccer",
                                EventKey: intent.ObservedEventId,
                                HomeTeam: string.Empty,
                                AwayTeam: string.Empty,
                                CommenceTime: commenceTime,
                                MarketType: PolymarketMarketType,
                                SelectionKey: intent.SelectionKey,
                                Stake: effectiveStake,
                                EntryPrice: (double)intent.CurrentReferencePrice,
                                CreatedAt: DateTime.UtcNow,
                                TargetSide: intent.TargetSide,
                                ObservedTeam: intent.ObservedTeam,
                                PolymarketConditionId: intent.PolymarketConditionId,
                                PolymarketEntryPrice: polymarketEntryPrice,
                                TargetProbability: (double)intent.TargetProbability,
                                TargetTokenId: intent.TargetTokenId),
                            stoppingToken);

                        if (positionId == Guid.Empty)
                        {
                            _logger.LogInformation(
                                "Duplicate Polymarket intent ignored. intentId={IntentId}",
                                intent.IntentId);

                            await _consumer.AckAsync(
                                _streams.PolymarketOrderIntents,
                                PolymarketGroupName,
                                msg.Id,
                                stoppingToken);
                            continue;
                        }

                        var afterEntryBalance = portfolio.CurrentBalance - effectiveStake;

                        await portfolioRepo.UpdateBalanceAsync(
                            afterEntryBalance,
                            DateTime.UtcNow,
                            stoppingToken);

                        var opened = new ExecutionReportV1(
                            SchemaVersion: "1.0.0",
                            ReportId: Guid.NewGuid().ToString("N"),
                            IntentId: intent.IntentId,
                            CorrelationId: $"{intent.ObservedEventId}|{intent.SelectionKey}|{intent.TargetSide}",
                            Ts: DateTime.UtcNow,
                            Status: "OPENED",
                            FilledPrice: polymarketEntryPrice ?? (double)intent.CurrentReferencePrice,
                            FilledUsd: effectiveStake,
                            TxHash: null,
                            Error: null);

                        await PersistAndPublishReportAsync(
                            opened,
                            reportRepo,
                            stoppingToken);

                        _logger.LogInformation(
                            "POLYMARKET_SIMULATED_POSITION opened. intentId={IntentId} conditionId={ConditionId} team={Team} sel={Sel} side={Side} direction={Direction} asiaProbability={AsiaProbability:F4} polymarketMid={PolymarketMid} target={Target:F4} comparableTarget={ComparableTarget} commenceTime={CommenceTime} stake={Stake} move={Move:F4}pp sources={Sources}",
                            intent.IntentId,
                            intent.PolymarketConditionId,
                            intent.ObservedTeam,
                            intent.SelectionKey,
                            intent.TargetSide,
                            intent.MovementDirection,
                            intent.CurrentReferencePrice,
                            polymarketEntryPrice.HasValue
                                ? polymarketEntryPrice.Value.ToString("F4", CultureInfo.InvariantCulture)
                                : "N/A",
                            intent.TargetProbability,
                            comparableTargetProbability.HasValue
                                ? comparableTargetProbability.Value.ToString("F4", CultureInfo.InvariantCulture)
                                : "N/A",
                            commenceTime.ToString("O"),
                            effectiveStake,
                            intent.MovementPercent,
                            intent.SupportingSources);

                        await _consumer.AckAsync(
                            _streams.PolymarketOrderIntents,
                            PolymarketGroupName,
                            msg.Id,
                            stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "Executor error processing msgId={MsgId}",
                            msg.Id);

                        await AckWithRetryAsync(
                            _streams.PolymarketOrderIntents,
                            PolymarketGroupName,
                            msg.Id,
                            stoppingToken);
                    }
                }
            }
        }

        private async Task PersistOrderIntentAsync(
            PolymarketOrderIntentV1 intent,
            IOrderIntentRepository repo,
            CancellationToken ct)
        {
            var orderIntent = new OrderIntentV1(
                SchemaVersion: "1.0.0",
                IntentId: intent.IntentId,
                CorrelationId: $"{intent.ObservedEventId}|{intent.SelectionKey}|{intent.TargetSide}",
                Ts: DateTime.UtcNow,
                Strategy: "polymarket_observed_signal",
                Venue: "POLYMARKET",
                SportKey: intent.SportKey ?? "unknown",
                EventKey: intent.ObservedEventId,
                HomeTeam: intent.ObservedTeam ?? string.Empty,
                AwayTeam: string.Empty,
                CommenceTime: ResolveCommenceTime(intent, DateTime.UtcNow),
                MarketType: PolymarketMarketType,
                SelectionKey: intent.SelectionKey,
                PriceLimit: (double)intent.CurrentReferencePrice,
                Stake: _risk.PolymarketFixedStakeUsd,
                Side: intent.TargetSide ?? string.Empty);

            await repo.InsertAsync(orderIntent, ct);
        }

        private async Task PersistRejectionAsync(
            IOrderIntentRejectionRepository repo,
            PolymarketOrderIntentV1 intent,
            string reason,
            double? entryMid,
            double? comparableTarget,
            double? headroomToTarget,
            double? timeToKickoffSeconds,
            string rawPayload,
            CancellationToken ct)
        {
            var rejection = new OrderIntentRejection(
                Id: Guid.NewGuid(),
                IntentId: intent.IntentId,
                SportKey: intent.SportKey ?? "unknown",
                ObservedTeam: intent.ObservedTeam ?? string.Empty,
                TargetSide: intent.TargetSide,
                PolymarketConditionId: intent.PolymarketConditionId,
                TargetTokenId: intent.TargetTokenId,
                Reason: reason,
                EntryMid: entryMid,
                ComparableTarget: comparableTarget,
                HeadroomToTarget: headroomToTarget,
                TimeToKickoffSeconds: timeToKickoffSeconds,
                CreatedAt: DateTime.UtcNow,
                RawPayload: rawPayload);

            await repo.InsertAsync(rejection, ct);
        }

        private async Task PersistAndPublishReportAsync(
            ExecutionReportV1 report,
            IExecutionReportRepository repo,
            CancellationToken ct)
        {
            await repo.InsertAsync(report, ct);

            var payload = JsonSerializer.Serialize(report);

            var fields = new Dictionary<string, string>
            {
                ["schema"] = "ExecutionReportV1",
                ["schemaVersion"] = report.SchemaVersion,
                ["reportId"] = report.ReportId,
                ["intentId"] = report.IntentId,
                ["correlationId"] = report.CorrelationId,
                ["ts"] = report.Ts.ToString("O"),
                ["status"] = report.Status,
                ["payload"] = payload
            };

            await _publisher.PublishAsync(_streams.ExecutionReports, fields, ct);
        }

        private static double? GetComparableTargetProbability(
            PolymarketOrderIntentV1 intent)
        {
            var rawTarget = (double)intent.TargetProbability;

            if (rawTarget <= 0) return 0;
            if (rawTarget >= 1) return 1;

            var targetSide = intent.TargetSide ?? string.Empty;

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

        private static DateTime? TryParseDateTime(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var result)
                ? result
                : null;
        }

        private async Task AckWithRetryAsync(
            string stream,
            string group,
            string messageId,
            CancellationToken ct)
        {
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    await _consumer.AckAsync(stream, group, messageId, ct);
                    return;
                }
                catch (RedisTimeoutException ex)
                {
                    _logger.LogWarning(
                        ex,
                        "ACK timeout for msgId={MsgId} attempt={Attempt}",
                        messageId,
                        attempt);

                    await Task.Delay(500 * attempt, ct);
                }
            }

            _logger.LogError(
                "ACK failed permanently for msgId={MsgId} stream={Stream}",
                messageId,
                stream);
        }

        private static DateTime ResolveCommenceTime(
            PolymarketOrderIntentV1 intent,
            DateTime utcNow)
        {
            var commenceTimeFromTick = TryParseDateTime(intent.CommenceTime);
            if (commenceTimeFromTick.HasValue)
            {
                return commenceTimeFromTick.Value;
            }

            var gameStartTimeFallback = TryParseDateTime(intent.GameStartTime);
            if (gameStartTimeFallback.HasValue)
            {
                return gameStartTimeFallback.Value;
            }

            var gammaFallback = TryParseDateTime(intent.MatchedGammaStartTime);
            if (gammaFallback.HasValue && gammaFallback.Value > utcNow)
            {
                return gammaFallback.Value;
            }

            return utcNow.AddHours(6);
        }
    }
}