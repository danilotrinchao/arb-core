using Arb.Core.Application.Abstractions.MarketData;
using Arb.Core.Application.Request;

namespace Arb.Core.Application.UseCases.MarketData
{
    public sealed class FootballMarketSubscriptionProvider : IFootballMarketSubscriptionProvider
    {
        private readonly IFootballMarketRegistry _registry;

        public FootballMarketSubscriptionProvider(
            IFootballMarketRegistry registry)
        {
            _registry = registry;
        }

        public IReadOnlyCollection<FootballMarketSubscriptionRequest> GetCurrentSubscriptions()
        {
            var candidates = _registry.GetQuoteCandidates();

            return candidates
                .Where(IsValidCandidate)
                .GroupBy(x => x.ConditionId, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .Select(x => new FootballMarketSubscriptionRequest
                {
                    CatalogId = x.CatalogId,
                    ConditionId = x.ConditionId,
                    Question = x.Question,
                    MarketSlug = x.MarketSlug,
                    GameStartTime = x.GameStartTime,
                    SemanticType = x.SemanticType,
                    ReferencedTeam = x.ReferencedTeam,
                    YesTokenId = x.YesTokenId,
                    NoTokenId = x.NoTokenId,
                    SideATokenId = x.SideATokenId,
                    SideBTokenId = x.SideBTokenId,
                    OutcomeRoleA = x.OutcomeRoleA,
                    OutcomeRoleB = x.OutcomeRoleB,
                    MatchedGammaId = x.MatchedGammaId,
                    MatchedGammaStartTime = x.MatchedGammaStartTime
                })
                .OrderBy(x => x.GameStartTime ?? "9999-12-31T23:59:59Z", StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.ConditionId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public IReadOnlyCollection<string> GetCurrentTokenIds()
        {
            return GetCurrentSubscriptions()
                .SelectMany(x => x.TokenIds)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        /// <summary>
        /// Valida candidate para entrar na slice ativa.
        /// Agora agnóstico quanto ao outcome role: aceita qualquer par de tokens válidos.
        /// - YES/NO (futebol legado)
        /// - SIDE_A/SIDE_B (NBA H2H)
        /// </summary>
        private static bool IsValidCandidate(FootballQuoteCandidate candidate)
        {
            if (candidate is null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(candidate.ConditionId))
            {
                return false;
            }

            // Precisa ter pelo menos um par válido de tokens
            var hasYesNo = !string.IsNullOrWhiteSpace(candidate.YesTokenId) &&
                          !string.IsNullOrWhiteSpace(candidate.NoTokenId);

            var hasSideAB = !string.IsNullOrWhiteSpace(candidate.SideATokenId) &&
                           !string.IsNullOrWhiteSpace(candidate.SideBTokenId);

            return hasYesNo || hasSideAB;
        }
    }
}
