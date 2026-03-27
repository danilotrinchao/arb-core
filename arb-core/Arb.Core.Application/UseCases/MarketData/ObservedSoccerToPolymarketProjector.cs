using Arb.Core.Application.Abstractions.MarketData;
using Arb.Core.Application.Request;
using Arb.Core.Contracts.Common.PolymarketObservation;
using System.Globalization;
using System.Text;

namespace Arb.Core.Application.UseCases.MarketData
{
    public class ObservedSoccerToPolymarketProjector : IObservedSoccerToPolymarketProjector
    {
        private const string TeamToWinSemanticType = "TEAM_TO_WIN_YES_NO";

        // Probabilidade mínima aceitável após conversão
        // Odds acima de 100 (prob < 0.01) são ruído ou erro de dados
        private const decimal MinValidProbability = 0.01m;

        // Probabilidade máxima aceitável
        // Odds abaixo de 1.01 (prob > 0.99) são praticamente certezas — fora do escopo
        private const decimal MaxValidProbability = 0.99m;

        private readonly IFootballMarketRegistry _registry;

        public ObservedSoccerToPolymarketProjector(
            IFootballMarketRegistry registry)
        {
            _registry = registry;
        }

        public IReadOnlyCollection<PolymarketObservedTickV1> Project(
            IReadOnlyCollection<ObservedSoccerSelectionSnapshot> observations,
            IReadOnlyCollection<FootballQuoteCandidate> activeCandidates)
        {
            if (observations.Count == 0 || activeCandidates.Count == 0)
                return Array.Empty<PolymarketObservedTickV1>();

            var result = new List<PolymarketObservedTickV1>();

            foreach (var observation in observations)
            {
                // Converte odds decimal para probabilidade implícita
                // antes de qualquer outra validação — se a conversão falhar,
                // não faz sentido projetar esse tick
                if (!TryConvertToImpliedProbability(observation.Price, out var impliedProbability))
                    continue;

                if (!TryResolveObservedTeam(observation, out var observedTeam))
                    continue;

                var normalizedObservedTeam = NormalizeTeam(observedTeam);
                if (string.IsNullOrWhiteSpace(normalizedObservedTeam))
                    continue;

                var matchingCandidates = activeCandidates
                    .Where(x =>
                        string.Equals(
                            NormalizeTeam(x.ReferencedTeam),
                            normalizedObservedTeam,
                            StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                foreach (var candidate in matchingCandidates)
                {
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

                        // Probabilidade implícita (0 a 1) em vez de odds decimal
                        // Compatível com a escala da Polymarket CLOB
                        ObservedPrice = impliedProbability,

                        PolymarketConditionId = candidate.ConditionId,
                        PolymarketCatalogId = candidate.CatalogId,
                        PolymarketMarketSlug = candidate.MarketSlug,
                        PolymarketQuestion = candidate.Question,
                        PolymarketSemanticType = candidate.SemanticType,
                        PolymarketReferencedTeam = candidate.ReferencedTeam,

                        TargetSide = "YES",
                        TargetTokenId = candidate.YesTokenId,
                        YesTokenId = candidate.YesTokenId,
                        NoTokenId = candidate.NoTokenId,

                        MatchedGammaId = candidate.MatchedGammaId,
                        MatchedGammaStartTime = candidate.MatchedGammaStartTime,

                        ProjectionReasonCode = "ACTIVE_SLICE_DIRECT_TEAM_TO_WIN_YES_MAPPING"
                    });
                }
            }

            // Deduplicação por ObservationId
            // ObservationId inclui BookmakerKey, então bookmakers distintos
            // geram IDs distintos e chegam separados ao engine
            return result
                .GroupBy(x => x.ObservationId, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .ToArray();
        }

        /// <summary>
        /// Converte odds decimal para probabilidade implícita.
        /// Retorna false se a odd for inválida (zero, negativa, ou resultado fora do range).
        /// </summary>
        private static bool TryConvertToImpliedProbability(
            decimal oddsDecimal,
            out decimal impliedProbability)
        {
            impliedProbability = 0m;

            // Odds decimal deve ser maior que 1.0 por definição
            // Odds = 1.0 significaria retorno zero (impossível em casas sérias)
            if (oddsDecimal <= 1.0m)
                return false;

            impliedProbability = Math.Round(1m / oddsDecimal, 6, MidpointRounding.AwayFromZero);

            // Valida o resultado — fora do range indica dado corrompido
            return impliedProbability >= MinValidProbability &&
                   impliedProbability <= MaxValidProbability;
        }

        private static string BuildObservationId(
            ObservedSoccerSelectionSnapshot observation,
            string conditionId)
        {
            // BookmakerKey incluído para garantir que cada fonte gera um tick distinto
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

            return false;
        }

        private static bool IsKickoffCompatible(
            string? observedCommenceTime,
            string? polymarketGameStartTime)
        {
            // Sem filtro de data — o matching é feito exclusivamente por nome de time.
            // Dois jogos do mesmo time em campeonatos diferentes são tratados como
            // oportunidades independentes — cada um com seu próprio conditionId
            // e posição separada no banco.
            // A data correta do jogo viaja no tick via MatchedGammaStartTime
            // e é gravada em CommenceTime na posição pelo executor.
            return true;
        }

        private static bool TryParseDate(string? value, out DateTimeOffset date)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                date = default;
                return false;
            }

            return DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out date);
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