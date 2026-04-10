using Arb.Core.Application.Abstractions.MarketData;
using Arb.Core.Contracts.Common.SoccerCatalog;

namespace Arb.Core.Application.UseCases.MarketData
{
    public sealed class FootballMarketRegistry : IFootballMarketRegistry
    {
        private readonly object _sync = new();

        private FootballQuoteEligibleSnapshotV1? _snapshot;

        private Dictionary<string, FootballCatalogMarketV1> _byConditionId =
            new(StringComparer.OrdinalIgnoreCase);

        public string? CurrentVersion
        {
            get
            {
                lock (_sync)
                {
                    return _snapshot?.Version;
                }
            }
        }

        public int Count
        {
            get
            {
                lock (_sync)
                {
                    return _byConditionId.Count;
                }
            }
        }

        public void ReplaceSnapshot(FootballQuoteEligibleSnapshotV1 snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            var next = snapshot.Markets
                .Where(x => !string.IsNullOrWhiteSpace(x.ConditionId))
                .GroupBy(x => x.ConditionId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    x => x.Key,
                    x => x.First(),
                    StringComparer.OrdinalIgnoreCase);

            lock (_sync)
            {
                _snapshot = snapshot;
                _byConditionId = next;
            }
        }

        public void MergeAdditionalMarkets(IEnumerable<FootballCatalogMarketV1> markets)
        {
            ArgumentNullException.ThrowIfNull(markets);

            lock (_sync)
            {
                foreach (var market in markets)
                {
                    if (!string.IsNullOrWhiteSpace(market.ConditionId))
                    {
                        _byConditionId[market.ConditionId] = market;
                    }
                }
            }
        }

        public FootballQuoteEligibleSnapshotV1? GetSnapshot()
        {
            lock (_sync)
            {
                return _snapshot;
            }
        }

        public IReadOnlyCollection<FootballCatalogMarketV1> GetAllMarkets()
        {
            lock (_sync)
            {
                return _byConditionId.Values.ToArray();
            }
        }

        public FootballCatalogMarketV1? GetByConditionId(string conditionId)
        {
            if (string.IsNullOrWhiteSpace(conditionId))
            {
                return null;
            }

            lock (_sync)
            {
                return _byConditionId.TryGetValue(conditionId, out var market)
                    ? market
                    : null;
            }
        }

        public IReadOnlyCollection<FootballQuoteCandidate> GetQuoteCandidates()
        {
            lock (_sync)
            {
                return _byConditionId.Values
                    .Select(ToQuoteCandidate)
                    .Where(x => x is not null)
                    .Cast<FootballQuoteCandidate>()
                    .ToArray();
            }
        }

        public void Clear()
        {
            lock (_sync)
            {
                _snapshot = null;
                _byConditionId.Clear();
            }
        }

        private static FootballQuoteCandidate? ToQuoteCandidate(FootballCatalogMarketV1 market)
        {
            var yes = market.Outcomes.FirstOrDefault(x =>
                string.Equals(x.BinaryOutcomeRole, "YES", StringComparison.OrdinalIgnoreCase));

            var no = market.Outcomes.FirstOrDefault(x =>
                string.Equals(x.BinaryOutcomeRole, "NO", StringComparison.OrdinalIgnoreCase));

            if (yes is not null && no is not null &&
                !string.IsNullOrWhiteSpace(yes.TokenId) &&
                !string.IsNullOrWhiteSpace(no.TokenId))
            {
                return new FootballQuoteCandidate
                {
                    CatalogId = market.CatalogId,
                    ConditionId = market.ConditionId,
                    Question = market.Question,
                    MarketSlug = market.MarketSlug,
                    GameStartTime = market.GameStartTime,
                    SemanticType = market.SemanticType,
                    ReferencedTeam = market.ReferencedTeam,
                    YesTokenId = yes.TokenId,
                    NoTokenId = no.TokenId,
                    OutcomeRoleA = "YES",
                    OutcomeRoleB = "NO",
                    MatchedGammaId = market.MatchedGammaId,
                    MatchedGammaStartTime = market.MatchedGammaStartTime
                };
            }

            var sideA = market.Outcomes.FirstOrDefault(x =>
                string.Equals(x.BinaryOutcomeRole, "SIDE_A", StringComparison.OrdinalIgnoreCase));

            var sideB = market.Outcomes.FirstOrDefault(x =>
                string.Equals(x.BinaryOutcomeRole, "SIDE_B", StringComparison.OrdinalIgnoreCase));

            if (sideA is not null && sideB is not null &&
                !string.IsNullOrWhiteSpace(sideA.TokenId) &&
                !string.IsNullOrWhiteSpace(sideB.TokenId))
            {
                return new FootballQuoteCandidate
                {
                    CatalogId = market.CatalogId,
                    ConditionId = market.ConditionId,
                    Question = market.Question,
                    MarketSlug = market.MarketSlug,
                    GameStartTime = market.GameStartTime,
                    SemanticType = market.SemanticType,
                    ReferencedTeam = market.ReferencedTeam,
                    YesTokenId = sideA.TokenId,
                    NoTokenId = sideB.TokenId,
                    SideATokenId = sideA.TokenId,
                    SideBTokenId = sideB.TokenId,
                    SideALabel = string.IsNullOrWhiteSpace(sideA.OutcomeLabel) ? null : sideA.OutcomeLabel,
                    SideBLabel = string.IsNullOrWhiteSpace(sideB.OutcomeLabel) ? null : sideB.OutcomeLabel,
                    OutcomeRoleA = "SIDE_A",
                    OutcomeRoleB = "SIDE_B",
                    MatchedGammaId = market.MatchedGammaId,
                    MatchedGammaStartTime = market.MatchedGammaStartTime
                };
            }

            return null;
        }
    }
}
