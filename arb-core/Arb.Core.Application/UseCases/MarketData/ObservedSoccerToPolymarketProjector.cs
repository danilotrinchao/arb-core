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
            var ticksGenerated = 0;
            var previewConditions = new List<string>();

            var result = new List<PolymarketObservedTickV1>();

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
                    out var observedSide);

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

                    result.Add(new PolymarketObservedTickV1
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
                    });

                    ticksGenerated++;
                    if (previewConditions.Count < 5)
                        previewConditions.Add(candidate.ConditionId);
                }
            }

            var deduped = result
                .GroupBy(x => x.ObservationId, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .ToArray();

            _logger.LogInformation(
                "Projector run. Observations={Obs} DiscardedSelectionKey={SelInvalid} DiscardedPriceInvalid={PriceInvalid} DiscardedTeamEmpty={TeamEmpty} DiscardedTeamMismatch={TeamMismatch} TicksGenerated={Ticks} PreviewConditions={Preview}",
                observationsReceived,
                discardedSelectionKey,
                discardedPriceInvalid,
                discardedTeamEmpty,
                discardedTeamMismatch,
                ticksGenerated,
                string.Join(",", previewConditions));

            return deduped;
        }

        /// <summary>
        /// Busca candidates que correspondem ao time observado normalizado.
        /// Para TEAM_TO_WIN_YES_NO (futebol): compara com ReferencedTeam.
        /// Para TEAM_VS_TEAM_WINNER (NBA H2H): compara com SideALabel e SideBLabel.
        /// </summary>
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
                // Futebol: YES/NO — compara com ReferencedTeam
                if (!string.Equals(
                    candidate.SemanticType,
                    TeamVsTeamSemanticType,
                    StringComparison.OrdinalIgnoreCase))
                {
                    // Validação: SelectionKey deve ser HOME ou AWAY para futebol
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

                // NBA: SIDE_A/SIDE_B — compara com SideALabel e SideBLabel
                // Validação: SelectionKey deve ser SIDE_A ou SIDE_B para NBA
                if (!string.Equals(selectionKey, "SIDE_A", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(selectionKey, "SIDE_B", StringComparison.OrdinalIgnoreCase))
                {
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
                }
            }

            return matches.ToArray();
        }

        /// <summary>
        /// Determina TargetSide e TargetTokenId baseado no semantic type, selection key
        /// e no time observado normalizado.
        /// 
        /// Para YES/NO (futebol): time observado → YES é o alvo
        /// Para SIDE_A/SIDE_B (NBA H2H):
        ///   - time corresponde a SideALabel → SIDE_A é o alvo
        ///   - time corresponde a SideBLabel → SIDE_B é o alvo
        /// </summary>
        private static void ResolveTargetSide(
            FootballQuoteCandidate candidate,
            string selectionKey,
            string normalizedObservedTeam,
            out string targetSide,
            out string targetTokenId)
        {
            // NBA H2H: SIDE_A/SIDE_B
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

                // Fallback (não deve acontecer se GetMatchingCandidates foi chamado antes)
                targetSide = "SIDE_A";
                targetTokenId = candidate.SideATokenId ?? string.Empty;
                return;
            }

            // Fallback: modelo YES/NO (legado futebol)
            // Observação de um time → YES é o alvo
            targetSide = "YES";
            targetTokenId = candidate.YesTokenId;
        }

        /// <summary>
        /// Determina o código de razão da projeção baseado no semantic type e no lado observado.
        /// </summary>
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

            // Futebol: HOME/AWAY
            if (string.Equals(
                    observation.SelectionKey, "HOME",
                    StringComparison.OrdinalIgnoreCase))
            {
                team = observation.HomeTeam;
                return !string.IsNullOrWhiteSpace(team);
            }

            if (string.Equals(
                    observation.SelectionKey, "AWAY",
                    StringComparison.OrdinalIgnoreCase))
            {
                team = observation.AwayTeam;
                return !string.IsNullOrWhiteSpace(team);
            }

            // NBA: SIDE_A/SIDE_B
            if (string.Equals(
                    observation.SelectionKey, "SIDE_A",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    observation.SelectionKey, "SIDE_B",
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
    }
}