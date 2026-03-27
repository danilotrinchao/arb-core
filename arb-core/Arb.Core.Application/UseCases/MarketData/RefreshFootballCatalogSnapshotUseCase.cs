using Arb.Core.Application.Abstractions.MarketData;
using Arb.Core.Contracts.Common.SoccerCatalog;
using Microsoft.Extensions.Logging;

namespace Arb.Core.Application.UseCases.MarketData
{
    public sealed class RefreshFootballCatalogSnapshotUseCase
    {
        private readonly IFootballCatalogRedisRepository _repository;
        private readonly IFootballMarketRegistry _registry;
        private readonly ILogger<RefreshFootballCatalogSnapshotUseCase> _logger;

        public RefreshFootballCatalogSnapshotUseCase(
            IFootballCatalogRedisRepository repository,
            IFootballMarketRegistry registry,
            ILogger<RefreshFootballCatalogSnapshotUseCase> logger)
        {
            _repository = repository;
            _registry = registry;
            _logger = logger;
        }

        public async Task<FootballQuoteEligibleSnapshotV1> ExecuteAsync(
            CancellationToken cancellationToken)
        {
            var snapshot = await _repository.GetCurrentSnapshotAsync(cancellationToken);

            if (snapshot is null)
            {
                throw new InvalidOperationException(
                    "Football catalog snapshot not found in Redis.");
            }

            _registry.ReplaceSnapshot(snapshot);

            _logger.LogInformation(
                "Football catalog snapshot loaded. Version={Version}, Markets={Count}",
                snapshot.Version,
                snapshot.Markets.Count);

            return snapshot;
        }
    }
}
