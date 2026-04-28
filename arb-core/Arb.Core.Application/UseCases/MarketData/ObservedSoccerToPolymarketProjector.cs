using Arb.Core.Application.Abstractions.MarketData;
using Arb.Core.Application.Request;
using Arb.Core.Contracts.Common.PolymarketObservation;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text;

namespace Arb.Core.Application.UseCases.MarketData
{
    public class ObservedSoccerToPolymarketProjector : IObservedSoccerToPolymarketProjector
    {
        private const string TeamToWinSemanticType = "TEAM_TO_WIN_YES_NO";
        private const string TeamVsTeamSemanticType = "TEAM_VS_TEAM_WINNER";

        private const decimal MinValidProbability = 0.01m;
        private const decimal MaxValidProbability = 0.99m;

        private readonly IFootballMarketRegistry _registry;
        private readonly ILogger<ObservedSoccerToPolymarketProjector> _logger;

        public ObservedSoccerToPolymarketProjector(
            IFootballMarketRegistry registry,
            ILogger<ObservedSoccerToPolymarketProjector> logger)
        {
            _registry = registry;
            _logger = logger;
        }

        public IReadOnlyCollection<PolymarketObservedTickV1> Project(
            IReadOnlyCollection<ObservedSoccerSelectionSnapshot> observations,
            IReadOnlyCollection<FootballQuoteCandidate> activeCandidates)
        {
            if (observations.Count == 0 || activeCandidates.Count == 0)
                return Array.Empty<PolymarketObservedTickV1>();

            var observationsReceived = observations.Count;
            var discardedSelectionKey = 0;
            var discardedPriceInvalid = 0;
            var discardedTeamEmpty = 0;
            var discardedTeamMismatch = 0;
            var discardedMissingTargetToken = 0;
            var rawTicksGenerated = 0;
            var duplicateCandidateTicks = 0;
            var replacedCandidateTicks = 0;

            var previewConditions = new List<string>();

            var selectedByEconomicProjectionKey =
                new Dictionary<string, ProjectedTickCandidate>(StringComparer.OrdinalIgnoreCase);

            foreach (var observation in observations)
            {
                if (!TryConvertToImpliedProbability(observation.Price, out var impliedProbability))
                {
                    discardedPriceInvalid++;
                    continue;
                }

                if (!TryResolveObservedTeam(observation, out var observedTeam))
                {
                    discardedSelectionKey++;
                    continue;
                }

                var normalizedObservedTeam = NormalizeTeam(observedTeam);
                if (string.IsNullOrWhiteSpace(normalizedObservedTeam))
                {
                    discardedTeamEmpty++;
                    continue;
                }

                var matchingCandidates = GetMatchingCandidates(
                    activeCandidates,
                    observation.SelectionKey,
                    normalizedObservedTeam,
                    out _);

                if (matchingCandidates.Count == 0)
                {
                    discardedTeamMismatch++;
                    continue;
                }

                foreach (var candidate in matchingCandidates)
                {
                    ResolveTargetSide(
                        candidate,
                        observation.SelectionKey,
                        normalizedObservedTeam,
                        out var targetSide,
                        out var targetTokenId);

                    if (string.IsNullOrWhiteSpace(targetSide) ||
                        string.IsNullOrWhiteSpace(targetTokenId))
                    {
                        discardedMissingTargetToken++;
                        continue;
                    }

                    var economicProjectionKey = BuildEconomicProjectionKey(
                        observation,
                        normalizedObservedTeam,
                        targetSide);

                    var tick = new PolymarketObservedTickV1
                    {
                        ObservationId = BuildObservationId(observation, candidate.ConditionId),
                        ObservedEventId = observation.EventId,
                        SportKey = observation.SportKey,
                        BookmakerKey = observation.BookmakerKey,
                        CommenceTime = observation.CommenceTime,
                        ObservedAt = observation.ObservedAt,
                        SelectionKey = observation.SelectionKey,
                        ObservedTeam = observedTeam,

                        ObservedPrice = impliedProbability,

                        PolymarketConditionId = candidate.ConditionId,
                        PolymarketCatalogId = candidate.CatalogId,
                        PolymarketMarketSlug = candidate.MarketSlug,
                        PolymarketQuestion = candidate.Question,
                        PolymarketSemanticType = candidate.SemanticType,
                        PolymarketReferencedTeam = candidate.ReferencedTeam,

                        TargetSide = targetSide,
                        TargetTokenId = targetTokenId,
                        YesTokenId = candidate.YesTokenId,
                        NoTokenId = candidate.NoTokenId,
                        SideATokenId = candidate.SideATokenId,
                        SideBTokenId = candidate.SideBTokenId,
                        OutcomeRoleA = candidate.OutcomeRoleA,
                        OutcomeRoleB = candidate.OutcomeRoleB,
                        SideALabel = candidate.SideALabel,
                        SideBLabel = candidate.SideBLabel,

                        MatchedGammaId = candidate.MatchedGammaId,
                        MatchedGammaStartTime = candidate.MatchedGammaStartTime,

                        ProjectionReasonCode = DetermineProjectionReason(candidate.SemanticType, targetSide)
                    };

                    var projected = new ProjectedTickCandidate(
                        Tick: tick,
                        Candidate: candidate,
                        Score: ScoreCandidate(
                            candidate,
                            observation,
                            normalizedObservedTeam,
                            targetSide,
                            targetTokenId));

                    rawTicksGenerated++;

                    if (previewConditions.Count < 5)
                        previewConditions.Add(candidate.ConditionId);

                    if (!selectedByEconomicProjectionKey.TryGetValue(economicProjectionKey, out var current))
                    {
                        selectedByEconomicProjectionKey[economicProjectionKey] = projected;
                        continue;
                    }

                    duplicateCandidateTicks++;

                    if (IsBetterCandidate(projected, current))
                    {
                        selectedByEconomicProjectionKey[economicProjectionKey] = projected;
                        replacedCandidateTicks++;
                    }
                }
            }

            var result = selectedByEconomicProjectionKey
                .Values
                .Select(x => x.Tick)
                .ToArray();

            _logger.LogInformation(
                "Projector run. Observations={Obs} DiscardedSelectionKey={SelInvalid} DiscardedPriceInvalid={PriceInvalid} DiscardedTeamEmpty={TeamEmpty} DiscardedTeamMismatch={TeamMismatch} DiscardedMissingTargetToken={MissingTargetToken} RawTicksGenerated={RawTicks} DedupedTicks={DedupedTicks} DuplicateCandidateTicks={DuplicateTicks} ReplacedCandidateTicks={ReplacedTicks} PreviewConditions={Preview}",
                observationsReceived,
                discardedSelectionKey,
                discardedPriceInvalid,
                discardedTeamEmpty,
                discardedTeamMismatch,
                discardedMissingTargetToken,
                rawTicksGenerated,
                result.Length,
                duplicateCandidateTicks,
                replacedCandidateTicks,
                string.Join(",", previewConditions));

            return result;
        }

        private IReadOnlyCollection<FootballQuoteCandidate> GetMatchingCandidates(
            IReadOnlyCollection<FootballQuoteCandidate> activeCandidates,
            string selectionKey,
            string normalizedObservedTeam,
            out string observedSide)
        {
            observedSide = string.Empty;
            var matches = new List<FootballQuoteCandidate>();

            foreach (var candidate in activeCandidates)
            {
                if (!string.Equals(
                        candidate.SemanticType,
                        TeamVsTeamSemanticType,
                        StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.Equals(selectionKey, "HOME", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(selectionKey, "AWAY", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (string.Equals(
                            NormalizeTeam(candidate.ReferencedTeam),
                            normalizedObservedTeam,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        matches.Add(candidate);
                    }

                    continue;
                }

                var normalizedSideA = NormalizeTeam(candidate.SideALabel);
                var normalizedSideB = NormalizeTeam(candidate.SideBLabel);

                if (string.Equals(normalizedSideA, normalizedObservedTeam, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(candidate);
                    observedSide = "SIDE_A";
                    continue;
                }

                if (string.Equals(normalizedSideB, normalizedObservedTeam, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(candidate);
                    observedSide = "SIDE_B";
                    continue;
                }

                if (TryMatchNicknameH2h(
                        normalizedObservedTeam,
                        normalizedSideA,
                        normalizedSideB,
                        out var matchedSide))
                {
                    matches.Add(candidate);
                    observedSide = matchedSide;
                }
            }

            return matches.ToArray();
        }

        private static void ResolveTargetSide(
            FootballQuoteCandidate candidate,
            string selectionKey,
            string normalizedObservedTeam,
            out string targetSide,
            out string targetTokenId)
        {
            if (string.Equals(
                    candidate.SemanticType,
                    TeamVsTeamSemanticType,
                    StringComparison.OrdinalIgnoreCase))
            {
                var normalizedSideA = NormalizeTeam(candidate.SideALabel);
                var normalizedSideB = NormalizeTeam(candidate.SideBLabel);

                if (string.Equals(normalizedSideA, normalizedObservedTeam, StringComparison.OrdinalIgnoreCase))
                {
                    targetSide = "SIDE_A";
                    targetTokenId = candidate.SideATokenId ?? string.Empty;
                    return;
                }

                if (string.Equals(normalizedSideB, normalizedObservedTeam, StringComparison.OrdinalIgnoreCase))
                {
                    targetSide = "SIDE_B";
                    targetTokenId = candidate.SideBTokenId ?? string.Empty;
                    return;
                }

                if (TryMatchNicknameH2h(
                        normalizedObservedTeam,
                        normalizedSideA,
                        normalizedSideB,
                        out var matchedSide))
                {
                    targetSide = matchedSide;
                    targetTokenId = string.Equals(matchedSide, "SIDE_A", StringComparison.OrdinalIgnoreCase)
                        ? (candidate.SideATokenId ?? string.Empty)
                        : (candidate.SideBTokenId ?? string.Empty);
                    return;
                }

                targetSide = string.Empty;
                targetTokenId = string.Empty;
                return;
            }

            targetSide = "YES";
            targetTokenId = candidate.YesTokenId;
        }

        private static int ScoreCandidate(
            FootballQuoteCandidate candidate,
            ObservedSoccerSelectionSnapshot observation,
            string normalizedObservedTeam,
            string targetSide,
            string targetTokenId)
        {
            var score = 0;

            if (!string.IsNullOrWhiteSpace(targetTokenId))
                score += 1000;

            if (!string.IsNullOrWhiteSpace(candidate.ConditionId))
                score += 100;

            if (!string.IsNullOrWhiteSpace(candidate.CatalogId))
                score += 25;

            if (!string.IsNullOrWhiteSpace(candidate.MatchedGammaId))
                score += 25;

            if (!string.IsNullOrWhiteSpace(candidate.MarketSlug))
                score += 10;

            if (!string.IsNullOrWhiteSpace(candidate.Question))
                score += 10;

            if (string.Equals(
                    candidate.SemanticType,
                    TeamToWinSemanticType,
                    StringComparison.OrdinalIgnoreCase))
            {
                score += 20;

                if (string.Equals(
                        NormalizeTeam(candidate.ReferencedTeam),
                        normalizedObservedTeam,
                        StringComparison.OrdinalIgnoreCase))
                {
                    score += 50;
                }
            }

            if (string.Equals(
                    candidate.SemanticType,
                    TeamVsTeamSemanticType,
                    StringComparison.OrdinalIgnoreCase))
            {
                score += 20;

                var normalizedSideA = NormalizeTeam(candidate.SideALabel);
                var normalizedSideB = NormalizeTeam(candidate.SideBLabel);

                if (string.Equals(targetSide, "SIDE_A", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(normalizedSideA, normalizedObservedTeam, StringComparison.OrdinalIgnoreCase))
                {
                    score += 50;
                }

                if (string.Equals(targetSide, "SIDE_B", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(normalizedSideB, normalizedObservedTeam, StringComparison.OrdinalIgnoreCase))
                {
                    score += 50;
                }
            }

            if (TryParseDateTime(candidate.MatchedGammaStartTime, out var gammaStart) &&
                TryParseDateTime(observation.CommenceTime, out var observationCommenceTime))
            {
                var deltaSeconds = Math.Abs((gammaStart - observationCommenceTime).TotalSeconds);

                if (deltaSeconds <= 5 * 60)
                    score += 50;
                else if (deltaSeconds <= 30 * 60)
                    score += 25;
                else if (deltaSeconds <= 2 * 60 * 60)
                    score += 10;
            }

            return score;
        }

        private static bool IsBetterCandidate(
            ProjectedTickCandidate candidate,
            ProjectedTickCandidate current)
        {
            if (candidate.Score != current.Score)
                return candidate.Score > current.Score;

            var candidateStart = TryParseDateTime(candidate.Candidate.MatchedGammaStartTime, out var parsedCandidateStart)
                ? parsedCandidateStart
                : DateTime.MaxValue;

            var currentStart = TryParseDateTime(current.Candidate.MatchedGammaStartTime, out var parsedCurrentStart)
                ? parsedCurrentStart
                : DateTime.MaxValue;

            if (candidateStart != currentStart)
                return candidateStart < currentStart;

            return string.Compare(
                candidate.Candidate.ConditionId,
                current.Candidate.ConditionId,
                StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static string BuildEconomicProjectionKey(
            ObservedSoccerSelectionSnapshot observation,
            string normalizedObservedTeam,
            string targetSide)
        {
            return string.Join(
                "|",
                NormalizeKeyComponent(observation.SportKey),
                NormalizeKeyComponent(observation.EventId),
                NormalizeKeyComponent(observation.SelectionKey),
                NormalizeKeyComponent(normalizedObservedTeam),
                NormalizeKeyComponent(targetSide),
                NormalizeKeyComponent(observation.BookmakerKey),
                NormalizeKeyComponent(observation.ObservedAt));
        }

        private static bool TryMatchNicknameH2h(
            string normalizedObservedTeam,
            string normalizedSideA,
            string normalizedSideB,
            out string matchedSide)
        {
            matchedSide = string.Empty;

            if (string.IsNullOrWhiteSpace(normalizedObservedTeam))
                return false;

            var tokens = normalizedObservedTeam.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            for (var i = tokens.Length - 1; i >= 0; i--)
            {
                var suffix = string.Join(" ", tokens.Skip(i));

                if (!string.IsNullOrWhiteSpace(normalizedSideA) &&
                    string.Equals(suffix, normalizedSideA, StringComparison.OrdinalIgnoreCase))
                {
                    matchedSide = "SIDE_A";
                    return true;
                }

                if (!string.IsNullOrWhiteSpace(normalizedSideB) &&
                    string.Equals(suffix, normalizedSideB, StringComparison.OrdinalIgnoreCase))
                {
                    matchedSide = "SIDE_B";
                    return true;
                }
            }

            return false;
        }

        private static string DetermineProjectionReason(string semanticType, string targetSide)
        {
            if (string.Equals(semanticType, TeamVsTeamSemanticType, StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(targetSide, "SIDE_A", StringComparison.OrdinalIgnoreCase)
                    ? "ACTIVE_SLICE_DIRECT_H2H_SIDE_A_MAPPING"
                    : "ACTIVE_SLICE_DIRECT_H2H_SIDE_B_MAPPING";
            }

            return "ACTIVE_SLICE_DIRECT_TEAM_TO_WIN_YES_MAPPING";
        }

        private static bool TryConvertToImpliedProbability(
            decimal oddsDecimal,
            out decimal impliedProbability)
        {
            impliedProbability = 0m;

            if (oddsDecimal <= 1.0m)
                return false;

            impliedProbability = Math.Round(1m / oddsDecimal, 6, MidpointRounding.AwayFromZero);

            return impliedProbability >= MinValidProbability &&
                   impliedProbability <= MaxValidProbability;
        }

        private static string BuildObservationId(
            ObservedSoccerSelectionSnapshot observation,
            string conditionId)
        {
            return string.Join(
                "::",
                observation.EventId,
                observation.SelectionKey,
                conditionId,
                observation.BookmakerKey ?? "unknown",
                observation.ObservedAt);
        }

        private static bool TryResolveObservedTeam(
            ObservedSoccerSelectionSnapshot observation,
            out string team)
        {
            team = string.Empty;

            if (string.Equals(
                    observation.SelectionKey,
                    "HOME",
                    StringComparison.OrdinalIgnoreCase))
            {
                team = observation.HomeTeam;
                return !string.IsNullOrWhiteSpace(team);
            }

            if (string.Equals(
                    observation.SelectionKey,
                    "AWAY",
                    StringComparison.OrdinalIgnoreCase))
            {
                team = observation.AwayTeam;
                return !string.IsNullOrWhiteSpace(team);
            }

            if (string.Equals(
                    observation.SelectionKey,
                    "SIDE_A",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    observation.SelectionKey,
                    "SIDE_B",
                    StringComparison.OrdinalIgnoreCase))
            {
                team = observation.DirectObservedTeam;
                return !string.IsNullOrWhiteSpace(team);
            }

            return false;
        }

        private static string NormalizeTeam(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var text = RemoveDiacritics(value)
                .ToLowerInvariant()
                .Replace(" futebol clube", " ")
                .Replace(" football club", " ")
                .Replace(" fc", " ")
                .Replace(" sc", " ")
                .Replace(" ec", " ")
                .Replace(" cf", " ")
                .Replace(" ac", " ")
                .Replace(" club", " ")
                .Replace(" de futbol", " ")
                .Replace(" futbol", " ")
                .Replace(" football", " ");

            var sb = new StringBuilder(text.Length);

            foreach (var ch in text)
            {
                sb.Append(char.IsLetterOrDigit(ch) || ch == ' ' ? ch : ' ');
            }

            return string.Join(
                " ",
                sb.ToString()
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries |
                                StringSplitOptions.TrimEntries));
        }

        private static string NormalizeKeyComponent(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "unknown";

            var parts = RemoveDiacritics(value)
                .ToLowerInvariant()
                .Trim()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            return string.Join(" ", parts);
        }

        private static bool TryParseDateTime(string? value, out DateTime result)
        {
            return DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out result);
        }

        private static string RemoveDiacritics(string text)
        {
            var normalized = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(normalized.Length);

            foreach (var ch in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) !=
                    UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(ch);
                }
            }

            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        private sealed record ProjectedTickCandidate(
            PolymarketObservedTickV1 Tick,
            FootballQuoteCandidate Candidate,
            int Score);
    }
}