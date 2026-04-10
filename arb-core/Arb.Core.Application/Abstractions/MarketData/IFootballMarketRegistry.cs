using Arb.Core.Application.UseCases.MarketData;
using Arb.Core.Contracts.Common.SoccerCatalog;

namespace Arb.Core.Application.Abstractions.MarketData
{
    public interface IFootballMarketRegistry
    {
        string? CurrentVersion { get; }

        int Count { get; }

        void ReplaceSnapshot(FootballQuoteEligibleSnapshotV1 snapshot);

        void MergeAdditionalMarkets(IEnumerable<FootballCatalogMarketV1> markets);

        FootballQuoteEligibleSnapshotV1? GetSnapshot();

        IReadOnlyCollection<FootballCatalogMarketV1> GetAllMarkets();

        FootballCatalogMarketV1? GetByConditionId(string conditionId);

        IReadOnlyCollection<FootballQuoteCandidate> GetQuoteCandidates();

        void Clear();
    }
}
