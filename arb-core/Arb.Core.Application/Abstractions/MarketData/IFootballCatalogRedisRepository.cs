using Arb.Core.Contracts.Common.SoccerCatalog;

namespace Arb.Core.Application.Abstractions.MarketData
{
    public interface IFootballCatalogRedisRepository
    {
        Task<FootballQuoteEligibleSnapshotV1?> GetCurrentSnapshotAsync(
            CancellationToken cancellationToken);

        Task<FootballQuoteEligibleSnapshotV1?> GetNbaSnapshotAsync(
           CancellationToken cancellationToken);

        Task<string> GetLatestCatalogStreamIdAsync(
            CancellationToken cancellationToken);

        Task<IReadOnlyCollection<FootballCatalogStreamEvent>> ReadCatalogEventsAsync(
            string afterStreamId,
            CancellationToken cancellationToken);
    }
}
