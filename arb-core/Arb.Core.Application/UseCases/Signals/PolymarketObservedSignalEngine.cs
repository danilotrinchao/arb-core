using Arb.Core.Application.Abstractions.Signals;
using Arb.Core.Contracts.Common.PolimarketSignals;
using Arb.Core.Contracts.Common.PolymarketObservation;
using Arb.Core.Infrastructure.Redis.SoccerCatalog;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace Arb.Core.Application.UseCases.Signals
{
    public class PolymarketObservedSignalEngine : IPolymarketObservedSignalEngine
    {
        private readonly ConcurrentDictionary<string, SignalBucket> _buckets =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly PolymarketObservedSignalOptions _options;
        private readonly ILogger<PolymarketObservedSignalEngine> _logger;

        public PolymarketObservedSignalEngine(
            IOptions<PolymarketObservedSignalOptions> options,
            ILogger<PolymarketObservedSignalEngine> logger)
        {
            _options = options.Value;
            _logger = logger;
        }

        public PolymarketOrderIntentV1? TryProcess(
            PolymarketObservedTickV1 tick,
            DateTime utcNow)
        {
            if (tick is null)
            {
                _logger.LogInformation(
                    "SignalEngine reject reason={Reason}",
                    "invalid_tick");

                return null;
            }

            if (string.IsNullOrWhiteSpace(tick.PolymarketConditionId))
            {
                _logger.LogInformation(
                    "SignalEngine reject reason={Reason}",
                    "missing_condition_id");

                return null;
            }

            var hasYesNo =
                !string.IsNullOrWhiteSpace(tick.YesTokenId) &&
                !string.IsNullOrWhiteSpace(tick.NoTokenId);

            var hasSideAB =
                !string.IsNullOrWhiteSpace(tick.SideATokenId) &&
                !string.IsNullOrWhiteSpace(tick.SideBTokenId);

            if (!hasYesNo && !hasSideAB)
            {
                _logger.LogInformation(
                    "SignalEngine reject reason={Reason} conditionId={ConditionId}",
                    "missing_token_ids",
                    tick.PolymarketConditionId);

                return null;
            }

            if (tick.ObservedPrice <= 0 || tick.ObservedPrice >= 1)
            {
                _logger.LogInformation(
                    "SignalEngine reject reason={Reason} conditionId={ConditionId} observedPrice={ObservedPrice}",
                    "observed_price_out_of_range",
                    tick.PolymarketConditionId,
                    tick.ObservedPrice);

                return null;
            }

            var bookmakerKey = string.IsNullOrWhiteSpace(tick.BookmakerKey)
                ? "unknown"
                : tick.BookmakerKey.Trim().ToLowerInvariant();

            var economicSignalKey = BuildEconomicSignalKey(tick);

            var bucket = _buckets.GetOrAdd(
                economicSignalKey,
                _ => new SignalBucket());

            lock (bucket.Sync)
            {
                bucket.LatestByBookmaker[bookmakerKey] = new SourceQuote
                {
                    Price = tick.ObservedPrice,
                    UpdatedAtUtc = utcNow
                };

                var currentPrices = bucket.LatestByBookmaker.Values
                    .Select(x => x.Price)
                    .Where(p => p > 0 && p < 1)
                    .OrderBy(p => p)
                    .ToArray();

                var supportingSources = currentPrices.Length;

                if (supportingSources < _options.MinSources)
                {
                    _logger.LogInformation(
                        "SignalEngine reject reason={Reason} economicKey={EconomicKey} conditionId={ConditionId} supportingSources={Sources} minSources={MinSources}",
                        "insufficient_sources",
                        economicSignalKey,
                        tick.PolymarketConditionId,
                        supportingSources,
                        _options.MinSources);

                    return null;
                }

                var currentReferencePrice = Median(currentPrices);
                var previousReferencePrice = bucket.ReferencePrice;

                bucket.ReferencePrice = currentReferencePrice;

                if (previousReferencePrice is null || previousReferencePrice.Value <= 0)
                {
                    _logger.LogInformation(
                        "SignalEngine info={Info} economicKey={EconomicKey} conditionId={ConditionId} currentReferencePrice={CurrentRef}",
                        "first_observation",
                        economicSignalKey,
                        tick.PolymarketConditionId,
                        currentReferencePrice);

                    return null;
                }

                if (currentReferencePrice == previousReferencePrice.Value)
                {
                    _logger.LogInformation(
                        "SignalEngine reject reason={Reason} economicKey={EconomicKey} conditionId={ConditionId} previousRef={PreviousRef} currentRef={CurrentRef}",
                        "no_price_change",
                        economicSignalKey,
                        tick.PolymarketConditionId,
                        previousReferencePrice.Value,
                        currentReferencePrice);

                    return null;
                }

                var movementAbsolute = Math.Abs(
                    currentReferencePrice - previousReferencePrice.Value);

                var movementPercent = Math.Abs(
                    (currentReferencePrice - previousReferencePrice.Value)
                    / previousReferencePrice.Value * 100m);

                var movementThreshold = (decimal)_options.MinMovementPct / 100m;

                if (movementAbsolute < movementThreshold)
                {
                    _logger.LogInformation(
                        "SignalEngine reject reason={Reason} economicKey={EconomicKey} conditionId={ConditionId} movementAbsolute={MovementAbs} threshold={Threshold}",
                        "movement_below_threshold",
                        economicSignalKey,
                        tick.PolymarketConditionId,
                        movementAbsolute,
                        movementThreshold);

                    return null;
                }

                if (bucket.LastIntentAtUtc is not null &&
                    utcNow - bucket.LastIntentAtUtc.Value <
                    TimeSpan.FromSeconds(_options.SignalCooldownSeconds))
                {
                    _logger.LogInformation(
                        "SignalEngine reject reason={Reason} economicKey={EconomicKey} conditionId={ConditionId} lastIntentAt={LastIntentAt} cooldownSeconds={Cooldown}",
                        "cooldown_active",
                        economicSignalKey,
                        tick.PolymarketConditionId,
                        bucket.LastIntentAtUtc,
                        _options.SignalCooldownSeconds);

                    return null;
                }

                var probabilityRose = currentReferencePrice > previousReferencePrice.Value;

                var movementDirection = probabilityRose
                    ? "ODDS_SHORTENING"
                    : "ODDS_DRIFTING";

                ResolveTargetForSignal(
                    tick,
                    probabilityRose,
                    out var targetSide,
                    out var targetTokenId);

                if (string.IsNullOrWhiteSpace(targetSide) ||
                    string.IsNullOrWhiteSpace(targetTokenId))
                {
                    _logger.LogInformation(
                        "SignalEngine reject reason={Reason} economicKey={EconomicKey} conditionId={ConditionId} observedSide={ObservedSide} resolvedSide={ResolvedSide}",
                        "missing_target_token_for_signal",
                        economicSignalKey,
                        tick.PolymarketConditionId,
                        tick.TargetSide,
                        targetSide);

                    return null;
                }

                bucket.LastIntentAtUtc = utcNow;

                _logger.LogInformation(
                    "SignalEngine generate_intent economicKey={EconomicKey} conditionId={ConditionId} supportingSources={Sources} previousReferencePrice={PrevRef} currentReferencePrice={CurrRef} movementAbsolute={MovementAbs} movementThreshold={Threshold} observedSide={ObservedSide} intentSide={IntentSide}",
                    economicSignalKey,
                    tick.PolymarketConditionId,
                    supportingSources,
                    previousReferencePrice.Value,
                    currentReferencePrice,
                    movementAbsolute,
                    movementThreshold,
                    tick.TargetSide,
                    targetSide);

                return new PolymarketOrderIntentV1
                {
                    IntentId = Guid.NewGuid().ToString("N"),
                    ObservationId = tick.ObservationId,
                    ObservedEventId = tick.ObservedEventId,
                    SportKey = tick.SportKey,
                    BookmakerKey = tick.BookmakerKey,
                    SelectionKey = tick.SelectionKey,
                    ObservedTeam = tick.ObservedTeam,
                    MovementDirection = movementDirection,
                    PreviousReferencePrice = previousReferencePrice.Value,
                    CurrentReferencePrice = currentReferencePrice,
                    TargetProbability = currentReferencePrice,
                    MovementPercent = Math.Round(
                        movementPercent,
                        4,
                        MidpointRounding.AwayFromZero),
                    SupportingSources = supportingSources,
                    PolymarketConditionId = tick.PolymarketConditionId,
                    PolymarketCatalogId = tick.PolymarketCatalogId,
                    PolymarketQuestion = tick.PolymarketQuestion,
                    PolymarketMarketSlug = tick.PolymarketMarketSlug,
                    TargetSide = targetSide,
                    TargetTokenId = targetTokenId,
                    YesTokenId = tick.YesTokenId,
                    NoTokenId = tick.NoTokenId,
                    MatchedGammaId = tick.MatchedGammaId,
                    MatchedGammaStartTime = tick.MatchedGammaStartTime,
                    CommenceTime = tick.CommenceTime,
                    GameStartTime = tick.CommenceTime,
                    ProjectionReasonCode = tick.ProjectionReasonCode,
                    GeneratedAt = utcNow.ToString("O")
                };
            }
        }

        private static string BuildEconomicSignalKey(PolymarketObservedTickV1 tick)
        {
            var eventComponent = !string.IsNullOrWhiteSpace(tick.ObservedEventId)
                ? tick.ObservedEventId
                : BuildEventFallbackComponent(tick);

            return string.Join(
                "|",
                NormalizeKeyComponent(tick.SportKey),
                NormalizeKeyComponent(eventComponent),
                NormalizeKeyComponent(tick.SelectionKey),
                NormalizeKeyComponent(tick.ObservedTeam),
                NormalizeKeyComponent(tick.TargetSide));
        }

        private static string BuildEventFallbackComponent(PolymarketObservedTickV1 tick)
        {
            if (!string.IsNullOrWhiteSpace(tick.PolymarketMarketSlug))
                return tick.PolymarketMarketSlug!;

            if (!string.IsNullOrWhiteSpace(tick.PolymarketQuestion))
                return tick.PolymarketQuestion;

            return tick.PolymarketConditionId;
        }

        private static string NormalizeKeyComponent(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "unknown";

            var parts = value
                .Trim()
                .ToLowerInvariant()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            return string.Join(" ", parts);
        }

        private static void ResolveTargetForSignal(
            PolymarketObservedTickV1 tick,
            bool probabilityRose,
            out string targetSide,
            out string targetTokenId)
        {
            var isSideABModel =
                !string.IsNullOrWhiteSpace(tick.SideATokenId) &&
                !string.IsNullOrWhiteSpace(tick.SideBTokenId);

            if (probabilityRose)
            {
                targetSide = tick.TargetSide;
                targetTokenId = tick.TargetTokenId;
                return;
            }

            if (isSideABModel)
            {
                if (string.Equals(tick.TargetSide, "SIDE_A", StringComparison.OrdinalIgnoreCase))
                {
                    targetSide = "SIDE_B";
                    targetTokenId = tick.SideBTokenId ?? string.Empty;
                    return;
                }

                if (string.Equals(tick.TargetSide, "SIDE_B", StringComparison.OrdinalIgnoreCase))
                {
                    targetSide = "SIDE_A";
                    targetTokenId = tick.SideATokenId ?? string.Empty;
                    return;
                }

                targetSide = string.Empty;
                targetTokenId = string.Empty;
                return;
            }

            if (string.Equals(tick.TargetSide, "YES", StringComparison.OrdinalIgnoreCase))
            {
                targetSide = "NO";
                targetTokenId = tick.NoTokenId;
                return;
            }

            if (string.Equals(tick.TargetSide, "NO", StringComparison.OrdinalIgnoreCase))
            {
                targetSide = "YES";
                targetTokenId = tick.YesTokenId;
                return;
            }

            targetSide = string.Empty;
            targetTokenId = string.Empty;
        }

        private static decimal Median(decimal[] sortedValues)
        {
            if (sortedValues.Length == 0)
                return 0m;

            var middle = sortedValues.Length / 2;

            return sortedValues.Length % 2 == 0
                ? (sortedValues[middle - 1] + sortedValues[middle]) / 2m
                : sortedValues[middle];
        }

        private sealed class SignalBucket
        {
            public object Sync { get; } = new();

            public Dictionary<string, SourceQuote> LatestByBookmaker { get; } =
                new(StringComparer.OrdinalIgnoreCase);

            public decimal? ReferencePrice { get; set; }

            public DateTime? LastIntentAtUtc { get; set; }
        }

        private sealed class SourceQuote
        {
            public decimal Price { get; init; }

            public DateTime UpdatedAtUtc { get; init; }
        }
    }
}