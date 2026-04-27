using Arb.Core.Contracts.Common.PolimarketSignals;
using Arb.Core.Contracts.Common.PolymarketObservation;
using Arb.Core.SignalEngine.Worker.Options;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace Arb.Core.SignalEngine.Worker.Services
{
    public sealed class ObservedSignalQualifier
    {
        private readonly SignalEngineOptions _options;
        private readonly HashSet<string> _restrictedLeagueKeys;
        private readonly HashSet<string> _preferredLeagueKeys;

        public ObservedSignalQualifier(IOptions<SignalEngineOptions> options)
        {
            _options = options.Value;

            _restrictedLeagueKeys = ParseCsvToSet(_options.RestrictedLeagueKeysCsv);
            _preferredLeagueKeys = ParseCsvToSet(_options.PreferredLeagueKeysCsv);
        }

        public QualificationResult Qualify(
            PolymarketObservedTickV1 tick,
            PolymarketOrderIntentV1 intent,
            DateTime utcNow)
        {
            var comparableTargetProbability = GetComparableTargetProbability(intent);

            decimal? initialEdge = null;
            decimal? deltaVsComparableTarget = null;

            if (comparableTargetProbability.HasValue)
            {
                initialEdge = comparableTargetProbability.Value - tick.ObservedPrice;
                deltaVsComparableTarget = tick.ObservedPrice - comparableTargetProbability.Value;
            }

            var commenceTime = ResolveCommenceTime(intent, utcNow);
            var timeToKickoffSeconds = (commenceTime - utcNow).TotalSeconds;
            var isLongHorizon = timeToKickoffSeconds > (_options.LongHorizonMinutes * 60);

            var leaguePolicyCategory = ResolveLeaguePolicyCategory(intent.SportKey);

            var signalQualityScore = CalculateSignalQualityScore(
                movementPercent: intent.MovementPercent,
                supportingSources: intent.SupportingSources,
                deltaVsComparableTarget: deltaVsComparableTarget,
                isLongHorizon: isLongHorizon,
                leaguePolicyCategory: leaguePolicyCategory);

            var signalRiskCategory = ResolveSignalRiskCategory(
                signalQualityScore,
                deltaVsComparableTarget,
                isLongHorizon,
                leaguePolicyCategory);

            return new QualificationResult(
                ComparableTargetProbability: comparableTargetProbability,
                InitialEdge: initialEdge,
                DeltaVsComparableTarget: deltaVsComparableTarget,
                TimeToKickoffSeconds: timeToKickoffSeconds,
                IsLongHorizon: isLongHorizon,
                LeaguePolicyCategory: leaguePolicyCategory,
                SignalQualityScore: signalQualityScore,
                SignalRiskCategory: signalRiskCategory);
        }

        private decimal? GetComparableTargetProbability(PolymarketOrderIntentV1 intent)
        {
            var rawTarget = intent.TargetProbability;

            if (rawTarget <= 0) return 0m;
            if (rawTarget >= 1) return 1m;

            var targetSide = intent.TargetSide ?? string.Empty;

            if (string.Equals(targetSide, "YES", StringComparison.OrdinalIgnoreCase))
            {
                return Round4(rawTarget);
            }

            if (string.Equals(targetSide, "NO", StringComparison.OrdinalIgnoreCase))
            {
                return Round4(1m - rawTarget);
            }

            if (string.Equals(targetSide, "SIDE_A", StringComparison.OrdinalIgnoreCase))
            {
                return Round4(rawTarget);
            }

            if (string.Equals(targetSide, "SIDE_B", StringComparison.OrdinalIgnoreCase))
            {
                return Round4(rawTarget);
            }

            return Round4(rawTarget);
        }

        private static decimal Round4(decimal value)
        {
            return Math.Round(value, 4, MidpointRounding.AwayFromZero);
        }

        private DateTime ResolveCommenceTime(
            PolymarketOrderIntentV1 intent,
            DateTime utcNow)
        {
            var commenceTimeFromTick = TryParseDateTime(intent.CommenceTime);
            if (commenceTimeFromTick.HasValue)
                return commenceTimeFromTick.Value;

            var gameStartTimeFallback = TryParseDateTime(intent.GameStartTime);
            if (gameStartTimeFallback.HasValue)
                return gameStartTimeFallback.Value;

            var gammaFallback = TryParseDateTime(intent.MatchedGammaStartTime);
            if (gammaFallback.HasValue && gammaFallback.Value > utcNow)
                return gammaFallback.Value;

            return utcNow.AddHours(6);
        }

        private string ResolveLeaguePolicyCategory(string? sportKey)
        {
            if (string.IsNullOrWhiteSpace(sportKey))
                return "NORMAL";

            if (_restrictedLeagueKeys.Contains(sportKey))
                return "RESTRICTED";

            if (_preferredLeagueKeys.Contains(sportKey))
                return "PREFERRED";

            return "NORMAL";
        }

        private double CalculateSignalQualityScore(
            decimal movementPercent,
            int supportingSources,
            decimal? deltaVsComparableTarget,
            bool isLongHorizon,
            string leaguePolicyCategory)
        {
            double score = 50.0;

            // supporting sources: até +20
            score += Math.Min(supportingSources * 5.0, 20.0);

            // movement percent: até +20
            score += Math.Min((double)movementPercent * 2.0, 20.0);

            // delta favorável
            if (deltaVsComparableTarget.HasValue && deltaVsComparableTarget.Value <= 0)
            {
                score += _options.ScoreBonusForNonPositiveDelta;
            }

            // long horizon
            if (isLongHorizon)
            {
                score -= _options.ScorePenaltyForLongHorizon;
            }

            // liga restrita
            if (string.Equals(leaguePolicyCategory, "RESTRICTED", StringComparison.OrdinalIgnoreCase))
            {
                score -= _options.ScorePenaltyForRestrictedLeague;
            }

            // preferred
            if (string.Equals(leaguePolicyCategory, "PREFERRED", StringComparison.OrdinalIgnoreCase))
            {
                score += 5.0;
            }

            score = Math.Max(0.0, Math.Min(100.0, score));

            return Math.Round(score, 2, MidpointRounding.AwayFromZero);
        }

        private string ResolveSignalRiskCategory(
            double signalQualityScore,
            decimal? deltaVsComparableTarget,
            bool isLongHorizon,
            string leaguePolicyCategory)
        {
            if (deltaVsComparableTarget.HasValue && deltaVsComparableTarget.Value > 0.03m)
                return "HIGH";

            if (isLongHorizon &&
                string.Equals(leaguePolicyCategory, "RESTRICTED", StringComparison.OrdinalIgnoreCase))
                return "HIGH";

            if (signalQualityScore >= 75)
                return "LOW";

            if (signalQualityScore >= 55)
                return "MEDIUM";

            return "HIGH";
        }

        private static DateTime? TryParseDateTime(string? value)
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

        private static HashSet<string> ParseCsvToSet(string? csv)
        {
            if (string.IsNullOrWhiteSpace(csv))
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            return csv
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        public sealed record QualificationResult(
            decimal? ComparableTargetProbability,
            decimal? InitialEdge,
            decimal? DeltaVsComparableTarget,
            double TimeToKickoffSeconds,
            bool IsLongHorizon,
            string LeaguePolicyCategory,
            double SignalQualityScore,
            string SignalRiskCategory);
    }
}