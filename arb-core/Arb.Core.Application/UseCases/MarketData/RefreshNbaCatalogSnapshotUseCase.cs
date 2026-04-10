using Arb.Core.Application.Abstractions.MarketData;
using Arb.Core.Contracts.Common.SoccerCatalog;
using Microsoft.Extensions.Logging;

namespace Arb.Core.Application.UseCases.MarketData
{
    public sealed class RefreshNbaCatalogSnapshotUseCase
    {
        private readonly IFootballCatalogRedisRepository _repository;
        private readonly IFootballMarketRegistry _registry;
        private readonly ILogger<RefreshNbaCatalogSnapshotUseCase> _logger;

        public RefreshNbaCatalogSnapshotUseCase(
            IFootballCatalogRedisRepository repository,
            IFootballMarketRegistry registry,
            ILogger<RefreshNbaCatalogSnapshotUseCase> logger)
        {
            _repository = repository;
            _registry = registry;
            _logger = logger;
        }

        public async Task<FootballQuoteEligibleSnapshotV1?> ExecuteAsync(
            CancellationToken cancellationToken)
        {
            var snapshot = await _repository.GetNbaSnapshotAsync(cancellationToken);

            if (snapshot is null)
            {
                _logger.LogWarning("NBA catalog snapshot not found in Redis.");
                return null;
            }

            _registry.MergeAdditionalMarkets(snapshot.Markets);

            _logger.LogInformation(
                "NBA catalog snapshot merged. Version={Version}, Markets={Count}",
                snapshot.Version,
                snapshot.Markets.Count);

            return snapshot;
        }
    }
}
