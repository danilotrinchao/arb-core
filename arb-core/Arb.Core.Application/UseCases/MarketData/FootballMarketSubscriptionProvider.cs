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

            if (string.IsNullOrWhiteSpace(candidate.YesTokenId))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(candidate.NoTokenId))
            {
                return false;
            }

            return true;
        }
    }
}
