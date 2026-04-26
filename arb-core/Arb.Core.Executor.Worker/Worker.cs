using Arb.Core.Application.Abstractions.Execution;
using Arb.Core.Application.Abstractions.Messaging;
using Arb.Core.Application.Abstractions.Persistence;
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

        private readonly ILogger<Worker> _logger;
        private readonly IStreamConsumer _consumer;
        private readonly IStreamPublisher _publisher;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly PolymarketClobPriceClient _clobPriceClient;
        private readonly IExecutionDispatchService _executionDispatchService;
        private readonly StreamsOptions _streams;
        private readonly ExecutorOptions _executorOptions;
        private readonly SettlementOptions _settlement;
        private readonly RiskOptions _risk;
        private readonly EntryQualityOptions _entryQuality;

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
            IExecutionDispatchService executionDispatchService,
            IOptions<StreamsOptions> streamsOptions,
            IOptions<ExecutorOptions> executorOptions,
            IOptions<SettlementOptions> settlementOptions,
            IOptions<RiskOptions> riskOptions,
            IOptions<EntryQualityOptions> entryQualityOptions)
        {
            _logger = logger;
            _consumer = consumer;
            _publisher = publisher;
            _scopeFactory = scopeFactory;
            _clobPriceClient = clobPriceClient;
            _executionDispatchService = executionDispatchService;
            _streams = streamsOptions.Value;
            _executorOptions = executorOptions.Value;
            _settlement = settlementOptions.Value;
            _risk = riskOptions.Value;
            _entryQuality = entryQualityOptions.Value;
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
                "Executor started. PolymarketConsuming={Stream} FixedStake={Stake} ExecutionMode={ExecutionMode}",
                _streams.PolymarketOrderIntents,
                _risk.PolymarketFixedStakeUsd,
                _executorOptions.ExecutionMode);

            _logger.LogInformation(
                "PolymarketExitMonitor started. Interval={Interval}s KickoffWindow={KickoffWindow}min EarlyExitWindow={EarlyExitWindow}min MinGapToTargetForEarlyExit={MinGap} MaxPriceAge={MaxAge}min",
                _settlement.ExitMonitorIntervalSeconds,
                _settlement.MinutesBeforeKickoffToClose,
                _settlement.MinutesBeforeKickoffToEarlyExit,
                _settlement.MinGapToTargetForEarlyExit,
                _settlement.MaxPriceAgeMinutes);

            _logger.LogInformation(
                "EntryQuality policy loaded. MinInitialEdgeGlobal={MinInitialEdgeGlobal:F4} LongHorizonMinutes={LongHorizonMinutes} MinInitialEdgeLongHorizon={MinInitialEdgeLongHorizon:F4} MaxPositiveDeltaToComparableTargetGlobal={MaxPositiveDeltaGlobal:F4} MaxPositiveDeltaToComparableTargetLongHorizon={MaxPositiveDeltaLongHorizon:F4} LaLigaSportKey={LaLigaSportKey} LigueOneSportKey={LigueOneSportKey}",
                _entryQuality.MinInitialEdgeGlobal,
                _entryQuality.LongHorizonMinutes,
                _entryQuality.MinInitialEdgeLongHorizon,
                _entryQuality.MaxPositiveDeltaToComparableTargetGlobal,
                _entryQuality.MaxPositiveDeltaToComparableTargetLongHorizon,
                _entryQuality.LaLigaSportKey,
                _entryQuality.LigueOneSportKey);

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

                        var utcNow = DateTime.UtcNow;
                        var intentGeneratedAt = ParseIntentGeneratedAt(intent.GeneratedAt);

                        double? intentAgeSeconds = intentGeneratedAt.HasValue
                            ? (utcNow - intentGeneratedAt.Value).TotalSeconds
                            : null;

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
                        var tokenHealthRepo = scope.ServiceProvider
                            .GetRequiredService<ITokenHealthRepository>();

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
                                rawTargetProbability: (double)intent.TargetProbability,
                                headroomToTarget: null,
                                initialEdge: null,
                                deltaVsComparableTarget: null,
                                timeToKickoffSeconds: null,
                                intentGeneratedAt: intentGeneratedAt,
                                intentAgeSeconds: intentAgeSeconds,
                                rawPayload: payload,
                                ct: stoppingToken);

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
                                rawTargetProbability: (double)intent.TargetProbability,
                                headroomToTarget: null,
                                initialEdge: null,
                                deltaVsComparableTarget: null,
                                timeToKickoffSeconds: null,
                                intentGeneratedAt: intentGeneratedAt,
                                intentAgeSeconds: intentAgeSeconds,
                                rawPayload: payload,
                                ct: stoppingToken);

                            await _consumer.AckAsync(
                                _streams.PolymarketOrderIntents,
                                PolymarketGroupName,
                                msg.Id,
                                stoppingToken);
                            continue;
                        }

                        var commenceTime = ResolveCommenceTime(intent, utcNow);
                        var timeToKickoff = commenceTime - utcNow;
                        var kickoffWindow = TimeSpan.FromMinutes(_settlement.MinutesBeforeKickoffToClose);

                        if (timeToKickoff <= kickoffWindow)
                        {
                            await PersistRejectionAsync(
                                rejectionRepo,
                                intent,
                                reason: "INSIDE_KICKOFF_WINDOW",
                                entryMid: null,
                                comparableTarget: null,
                                rawTargetProbability: (double)intent.TargetProbability,
                                headroomToTarget: null,
                                initialEdge: null,
                                deltaVsComparableTarget: null,
                                timeToKickoffSeconds: timeToKickoff.TotalSeconds,
                                intentGeneratedAt: intentGeneratedAt,
                                intentAgeSeconds: intentAgeSeconds,
                                rawPayload: payload,
                                ct: stoppingToken);

                            await _consumer.AckAsync(
                                _streams.PolymarketOrderIntents,
                                PolymarketGroupName,
                                msg.Id,
                                stoppingToken);
                            continue;
                        }

                        if (!string.IsNullOrWhiteSpace(intent.TargetTokenId))
                        {
                            var isBlocked = await tokenHealthRepo.IsBlockedAsync(
                                intent.TargetTokenId,
                                utcNow,
                                stoppingToken);

                            if (isBlocked)
                            {
                                await PersistRejectionAsync(
                                    rejectionRepo,
                                    intent,
                                    reason: "TOKEN_NO_ORDERBOOK_404",
                                    entryMid: null,
                                    comparableTarget: null,
                                    rawTargetProbability: (double)intent.TargetProbability,
                                    headroomToTarget: null,
                                    initialEdge: null,
                                    deltaVsComparableTarget: null,
                                    timeToKickoffSeconds: timeToKickoff.TotalSeconds,
                                    intentGeneratedAt: intentGeneratedAt,
                                    intentAgeSeconds: intentAgeSeconds,
                                    rawPayload: payload,
                                    ct: stoppingToken);

                                await _consumer.AckAsync(
                                    _streams.PolymarketOrderIntents,
                                    PolymarketGroupName,
                                    msg.Id,
                                    stoppingToken);
                                continue;
                            }
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
                            }
                        }

                        if (!polymarketEntryPrice.HasValue)
                        {
                            await PersistRejectionAsync(
                                rejectionRepo,
                                intent,
                                reason: "ENTRY_PRICE_UNAVAILABLE",
                                entryMid: null,
                                comparableTarget: null,
                                rawTargetProbability: (double)intent.TargetProbability,
                                headroomToTarget: null,
                                initialEdge: null,
                                deltaVsComparableTarget: null,
                                timeToKickoffSeconds: timeToKickoff.TotalSeconds,
                                intentGeneratedAt: intentGeneratedAt,
                                intentAgeSeconds: intentAgeSeconds,
                                rawPayload: payload,
                                ct: stoppingToken);

                            await _consumer.AckAsync(
                                _streams.PolymarketOrderIntents,
                                PolymarketGroupName,
                                msg.Id,
                                stoppingToken);
                            continue;
                        }

                        var comparableTargetProbability = GetComparableTargetProbability(intent);

                        if (comparableTargetProbability.HasValue)
                        {
                            var initialEdge = comparableTargetProbability.Value - polymarketEntryPrice.Value;
                            var deltaVsComparableTarget = polymarketEntryPrice.Value - comparableTargetProbability.Value;

                            var entryDecision = EvaluateEntryQuality(
                                sportKey: intent.SportKey ?? "unknown",
                                initialEdge: initialEdge,
                                deltaVsComparableTarget: deltaVsComparableTarget,
                                timeToKickoff: timeToKickoff);

                            if (!entryDecision.Allow)
                            {
                                await PersistRejectionAsync(
                                    rejectionRepo,
                                    intent,
                                    reason: entryDecision.Reason,
                                    entryMid: polymarketEntryPrice,
                                    comparableTarget: comparableTargetProbability,
                                    rawTargetProbability: (double)intent.TargetProbability,
                                    headroomToTarget: initialEdge,
                                    initialEdge: initialEdge,
                                    deltaVsComparableTarget: deltaVsComparableTarget,
                                    timeToKickoffSeconds: timeToKickoff.TotalSeconds,
                                    intentGeneratedAt: intentGeneratedAt,
                                    intentAgeSeconds: intentAgeSeconds,
                                    rawPayload: payload,
                                    ct: stoppingToken);

                                _logger.LogInformation(
                                    "Entry rejected by quality policy. intentId={IntentId} sportKey={SportKey} team={Team} targetSide={TargetSide} comparableTarget={ComparableTarget:F4} polyEntry={PolyEntry:F4} initialEdge={InitialEdge:F4} deltaVsComparableTarget={DeltaVsComparableTarget:F4} timeToKickoff={TimeToKickoff} reason={Reason}",
                                    intent.IntentId,
                                    intent.SportKey,
                                    intent.ObservedTeam,
                                    intent.TargetSide,
                                    comparableTargetProbability.Value,
                                    polymarketEntryPrice.Value,
                                    initialEdge,
                                    deltaVsComparableTarget,
                                    timeToKickoff.ToString(@"dd\.hh\:mm\:ss"),
                                    entryDecision.Reason);

                                await _consumer.AckAsync(
                                    _streams.PolymarketOrderIntents,
                                    PolymarketGroupName,
                                    msg.Id,
                                    stoppingToken);
                                continue;
                            }
                        }

                        var hasDuplicateOpenPosition = await positionRepo.ExistsOpenDuplicateAsync(
                            intent.SportKey ?? "soccer",
                            intent.ObservedEventId,
                            intent.PolymarketConditionId,
                            intent.TargetTokenId,
                            intent.TargetSide,
                            stoppingToken);

                        if (hasDuplicateOpenPosition)
                        {
                            double? initialEdge = null;
                            double? deltaVsComparableTarget = null;

                            if (comparableTargetProbability.HasValue && polymarketEntryPrice.HasValue)
                            {
                                initialEdge = comparableTargetProbability.Value - polymarketEntryPrice.Value;
                                deltaVsComparableTarget = polymarketEntryPrice.Value - comparableTargetProbability.Value;
                            }

                            await PersistRejectionAsync(
                                rejectionRepo,
                                intent,
                                reason: "DUPLICATE_OPEN_POSITION",
                                entryMid: polymarketEntryPrice,
                                comparableTarget: comparableTargetProbability,
                                rawTargetProbability: (double)intent.TargetProbability,
                                headroomToTarget: initialEdge,
                                initialEdge: initialEdge,
                                deltaVsComparableTarget: deltaVsComparableTarget,
                                timeToKickoffSeconds: timeToKickoff.TotalSeconds,
                                intentGeneratedAt: intentGeneratedAt,
                                intentAgeSeconds: intentAgeSeconds,
                                rawPayload: payload,
                                ct: stoppingToken);

                            await _consumer.AckAsync(
                                _streams.PolymarketOrderIntents,
                                PolymarketGroupName,
                                msg.Id,
                                stoppingToken);

                            continue;
                        }

                        if (IsRealExecutionMode())
                        {
                            var initialEdge = comparableTargetProbability.HasValue
                                ? comparableTargetProbability.Value - polymarketEntryPrice.Value
                                : (double?)null;

                            var deltaVsComparableTarget = comparableTargetProbability.HasValue
                                ? polymarketEntryPrice.Value - comparableTargetProbability.Value
                                : (double?)null;

                            var executionCommand = new ExecutionCommandDto
                            {
                                RequestId = Guid.NewGuid(),
                                IntentId = Guid.TryParse(intent.IntentId, out var parsedIntentId)
                                    ? parsedIntentId
                                    : null,
                                PositionId = null,
                                TokenId = intent.TargetTokenId ?? string.Empty,
                                MarketConditionId = intent.PolymarketConditionId,
                                Side = "BUY",
                                Price = polymarketEntryPrice.Value,
                                SizeUsd = effectiveStake,
                                TimeInForce = "GTC",
                                CorrelationId = $"{intent.ObservedEventId}|{intent.SelectionKey}|{intent.TargetSide}",
                                Metadata = new Dictionary<string, object>
                                {
                                    ["sportKey"] = intent.SportKey ?? "soccer",
                                    ["eventKey"] = intent.ObservedEventId,
                                    ["observedTeam"] = intent.ObservedTeam ?? string.Empty,
                                    ["selectionKey"] = intent.SelectionKey,
                                    ["targetSide"] = intent.TargetSide ?? string.Empty,
                                    ["reason"] = "ENTRY_SIGNAL",
                                    ["rawIntentId"] = intent.IntentId,
                                    ["commenceTime"] = commenceTime.ToString("O"),
                                    ["targetProbability"] = intent.TargetProbability,
                                    ["comparableTargetProbability"] = comparableTargetProbability?.ToString("F6", CultureInfo.InvariantCulture) ?? string.Empty,
                                    ["initialEdge"] = initialEdge?.ToString("F6", CultureInfo.InvariantCulture) ?? string.Empty,
                                    ["deltaVsComparableTarget"] = deltaVsComparableTarget?.ToString("F6", CultureInfo.InvariantCulture) ?? string.Empty
                                }
                            };

                            var executionResponse = await _executionDispatchService.DispatchBuyAsync(
                                executionCommand,
                                stoppingToken);

                            if (!executionResponse.Success)
                            {
                                await PersistRejectionAsync(
                                    rejectionRepo,
                                    intent,
                                    reason: $"EXECUTION_{executionResponse.Status}",
                                    entryMid: polymarketEntryPrice,
                                    comparableTarget: comparableTargetProbability,
                                    rawTargetProbability: (double)intent.TargetProbability,
                                    headroomToTarget: initialEdge,
                                    initialEdge: initialEdge,
                                    deltaVsComparableTarget: deltaVsComparableTarget,
                                    timeToKickoffSeconds: timeToKickoff.TotalSeconds,
                                    intentGeneratedAt: intentGeneratedAt,
                                    intentAgeSeconds: intentAgeSeconds,
                                    rawPayload: payload,
                                    ct: stoppingToken);

                                _logger.LogWarning(
                                    "Execution dispatch failed/rejected. intentId={IntentId} requestId={RequestId} status={Status} errorCode={ErrorCode} errorMessage={ErrorMessage}",
                                    intent.IntentId,
                                    executionCommand.RequestId,
                                    executionResponse.Status,
                                    executionResponse.ErrorCode,
                                    executionResponse.ErrorMessage);

                                await _consumer.AckAsync(
                                    _streams.PolymarketOrderIntents,
                                    PolymarketGroupName,
                                    msg.Id,
                                    stoppingToken);
                                continue;
                            }

                            _logger.LogInformation(
                                "Execution accepted. intentId={IntentId} requestId={RequestId} externalOrderId={ExternalOrderId}",
                                intent.IntentId,
                                executionCommand.RequestId,
                                executionResponse.ExternalOrderId);

                            await _consumer.AckAsync(
                                _streams.PolymarketOrderIntents,
                                PolymarketGroupName,
                                msg.Id,
                                stoppingToken);
                            continue;
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
                                PolymarketEntryPrice: polymarketEntryPrice.Value,
                                TargetProbability: (double)intent.TargetProbability,
                                TargetTokenId: intent.TargetTokenId),
                            stoppingToken);

                        if (positionId == Guid.Empty)
                        {
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
                            FilledPrice: polymarketEntryPrice.Value,
                            FilledUsd: effectiveStake,
                            TxHash: null,
                            Error: null);

                        await PersistAndPublishReportAsync(
                            opened,
                            reportRepo,
                            stoppingToken);

                        if (comparableTargetProbability.HasValue)
                        {
                            var initialEdge = comparableTargetProbability.Value - polymarketEntryPrice.Value;
                            var deltaVsComparableTarget = polymarketEntryPrice.Value - comparableTargetProbability.Value;

                            _logger.LogInformation(
                                "Paper entry opened. intentId={IntentId} sportKey={SportKey} team={Team} targetSide={TargetSide} comparableTarget={ComparableTarget:F4} polyEntry={PolyEntry:F4} initialEdge={InitialEdge:F4} deltaVsComparableTarget={DeltaVsComparableTarget:F4} timeToKickoff={TimeToKickoff}",
                                intent.IntentId,
                                intent.SportKey,
                                intent.ObservedTeam,
                                intent.TargetSide,
                                comparableTargetProbability.Value,
                                polymarketEntryPrice.Value,
                                initialEdge,
                                deltaVsComparableTarget,
                                timeToKickoff.ToString(@"dd\.hh\:mm\:ss"));
                        }

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

        private EntryDecision EvaluateEntryQuality(
            string sportKey,
            double initialEdge,
            double deltaVsComparableTarget,
            TimeSpan timeToKickoff)
        {
            var isLongHorizon = timeToKickoff > TimeSpan.FromMinutes(_entryQuality.LongHorizonMinutes);

            var isLaLiga = string.Equals(
                sportKey,
                _entryQuality.LaLigaSportKey,
                StringComparison.OrdinalIgnoreCase);

            var isLigueOne = string.Equals(
                sportKey,
                _entryQuality.LigueOneSportKey,
                StringComparison.OrdinalIgnoreCase);

            if (isLaLiga && _entryQuality.RejectPositiveDeltaForLaLiga && deltaVsComparableTarget > 0)
            {
                return EntryDecision.Reject("LEAGUE_POSITIVE_DELTA_NOT_ALLOWED");
            }

            if (isLigueOne && _entryQuality.RejectPositiveDeltaForLigueOne && deltaVsComparableTarget > 0)
            {
                return EntryDecision.Reject("LEAGUE_POSITIVE_DELTA_NOT_ALLOWED");
            }

            if (deltaVsComparableTarget >= _entryQuality.MaxPositiveDeltaToComparableTargetGlobal)
            {
                return EntryDecision.Reject("DELTA_VS_COMPARABLE_TARGET_TOO_HIGH");
            }

            if (isLongHorizon &&
                deltaVsComparableTarget >= _entryQuality.MaxPositiveDeltaToComparableTargetLongHorizon)
            {
                return EntryDecision.Reject("LONG_HORIZON_DELTA_VS_COMPARABLE_TARGET_TOO_HIGH");
            }

            var minRequiredEdge = _entryQuality.MinInitialEdgeGlobal;

            if (isLongHorizon)
            {
                minRequiredEdge = Math.Max(
                    minRequiredEdge,
                    _entryQuality.MinInitialEdgeLongHorizon);
            }

            if (isLaLiga)
            {
                minRequiredEdge = Math.Max(
                    minRequiredEdge,
                    _entryQuality.LaLigaMinInitialEdgeGlobal);

                if (isLongHorizon)
                {
                    minRequiredEdge = Math.Max(
                        minRequiredEdge,
                        _entryQuality.LaLigaMinInitialEdgeLongHorizon);
                }
            }

            if (isLigueOne)
            {
                minRequiredEdge = Math.Max(
                    minRequiredEdge,
                    _entryQuality.LigueOneMinInitialEdgeGlobal);

                if (isLongHorizon)
                {
                    minRequiredEdge = Math.Max(
                        minRequiredEdge,
                        _entryQuality.LigueOneMinInitialEdgeLongHorizon);
                }
            }

            if (initialEdge < minRequiredEdge)
            {
                if (isLaLiga && isLongHorizon)
                    return EntryDecision.Reject("LEAGUE_LONG_HORIZON_MIN_EDGE_NOT_MET");

                if (isLaLiga)
                    return EntryDecision.Reject("LEAGUE_MIN_EDGE_NOT_MET");

                if (isLigueOne && isLongHorizon)
                    return EntryDecision.Reject("LEAGUE_LONG_HORIZON_MIN_EDGE_NOT_MET");

                if (isLigueOne)
                    return EntryDecision.Reject("LEAGUE_MIN_EDGE_NOT_MET");

                if (isLongHorizon)
                    return EntryDecision.Reject("INITIAL_EDGE_BELOW_LONG_HORIZON_MINIMUM");

                return EntryDecision.Reject("INITIAL_EDGE_BELOW_GLOBAL_MINIMUM");
            }

            return EntryDecision.AllowOpen();
        }

        private bool IsRealExecutionMode()
        {
            return string.Equals(
                _executorOptions.ExecutionMode,
                "Real",
                StringComparison.OrdinalIgnoreCase);
        }

        private async Task PersistOrderIntentAsync(
            PolymarketOrderIntentV1 intent,
            IOrderIntentRepository repo,
            CancellationToken ct)
        {
            var nowUtc = DateTime.UtcNow;

            var orderIntent = new OrderIntentV1(
                SchemaVersion: "1.0.0",
                IntentId: intent.IntentId,
                CorrelationId: $"{intent.ObservedEventId}|{intent.SelectionKey}|{intent.TargetSide}",
                Ts: nowUtc,
                Strategy: "polymarket_observed_signal",
                Venue: "POLYMARKET",
                SportKey: intent.SportKey ?? "unknown",
                EventKey: intent.ObservedEventId,
                HomeTeam: intent.ObservedTeam ?? string.Empty,
                AwayTeam: string.Empty,
                CommenceTime: ResolveCommenceTime(intent, nowUtc),
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
            double? rawTargetProbability,
            double? headroomToTarget,
            double? initialEdge,
            double? deltaVsComparableTarget,
            double? timeToKickoffSeconds,
            DateTime? intentGeneratedAt,
            double? intentAgeSeconds,
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
                RawTargetProbability: rawTargetProbability,
                HeadroomToTarget: headroomToTarget,
                InitialEdge: initialEdge,
                DeltaVsComparableTarget: deltaVsComparableTarget,
                TimeToKickoffSeconds: timeToKickoffSeconds,
                IntentGeneratedAt: intentGeneratedAt,
                IntentAgeSeconds: intentAgeSeconds,
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

            if (string.Equals(targetSide, "YES", StringComparison.OrdinalIgnoreCase))
            {
                return Math.Round(rawTarget, 4, MidpointRounding.AwayFromZero);
            }

            if (string.Equals(targetSide, "NO", StringComparison.OrdinalIgnoreCase))
            {
                return Math.Round(1d - rawTarget, 4, MidpointRounding.AwayFromZero);
            }

            if (string.Equals(targetSide, "SIDE_A", StringComparison.OrdinalIgnoreCase))
            {
                return Math.Round(rawTarget, 4, MidpointRounding.AwayFromZero);
            }

            if (string.Equals(targetSide, "SIDE_B", StringComparison.OrdinalIgnoreCase))
            {
                return Math.Round(rawTarget, 4, MidpointRounding.AwayFromZero);
            }

            return Math.Round(rawTarget, 4, MidpointRounding.AwayFromZero);
        }

        private static DateTime? ParseIntentGeneratedAt(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed)
                ? parsed
                : null;
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

        private readonly record struct EntryDecision(
            bool Allow,
            string Reason)
        {
            public static EntryDecision AllowOpen() => new(true, string.Empty);
            public static EntryDecision Reject(string reason) => new(false, reason);
        }
    }
}