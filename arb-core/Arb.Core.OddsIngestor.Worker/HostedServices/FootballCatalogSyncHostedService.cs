using Arb.Core.Application.Abstractions.MarketData;
using Arb.Core.Application.Request;
using Arb.Core.Application.UseCases.MarketData;
using Arb.Core.Infrastructure.Redis.SoccerCatalog;
using Microsoft.Extensions.Options;

namespace Arb.Core.OddsIngestor.Worker.HostedServices
{
    public class FootballCatalogSyncHostedService : BackgroundService
    {
        private readonly IFootballCatalogRedisRepository _repository;
        private readonly RefreshFootballCatalogSnapshotUseCase _refreshUseCase;
        private readonly IFootballMarketSubscriptionProvider _subscriptionProvider;
        private readonly FootballCatalogRedisOptions _options;
        private readonly ILogger<FootballCatalogSyncHostedService> _logger;

        public FootballCatalogSyncHostedService(
            IFootballCatalogRedisRepository repository,
            RefreshFootballCatalogSnapshotUseCase refreshUseCase,
            IFootballMarketSubscriptionProvider subscriptionProvider,
            IOptions<FootballCatalogRedisOptions> options,
            ILogger<FootballCatalogSyncHostedService> logger)
        {
            _repository = repository;
            _refreshUseCase = refreshUseCase;
            _subscriptionProvider = subscriptionProvider;
            _options = options.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Football catalog sync service starting");

            var lastSeenStreamId = await _repository.GetLatestCatalogStreamIdAsync(stoppingToken);
            var initialSnapshot = await _refreshUseCase.ExecuteAsync(stoppingToken);

            LogSubscriptionProjection(
                "Initial football catalog synced",
                initialSnapshot.Version,
                lastSeenStreamId);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var events = await _repository.ReadCatalogEventsAsync(
                        lastSeenStreamId,
                        stoppingToken);

                    if (events.Count == 0)
                    {
                        // Sem este delay, o loop hammers o Redis em busy-wait
                        // e gera RedisTimeoutException por saturação do multiplexer.
                        await Task.Delay(
                            TimeSpan.FromMilliseconds(_options.PollingIntervalMs),
                            stoppingToken);
                        continue;
                    }

                    foreach (var evt in events)
                    {
                        lastSeenStreamId = evt.StreamEntryId;

                        if (!string.Equals(
                                evt.EventType,
                                _options.UpdatedEventType,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var snapshot = await _refreshUseCase.ExecuteAsync(stoppingToken);

                        LogSubscriptionProjection(
                            "Football catalog reloaded from Redis stream",
                            snapshot.Version,
                            evt.StreamEntryId);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error while syncing football catalog from Redis");
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                }
            }

            _logger.LogInformation("Football catalog sync service stopped");
        }

        private void LogSubscriptionProjection(
            string message,
            string version,
            string streamId)
        {
            var subscriptions = _subscriptionProvider.GetCurrentSubscriptions();
            var tokenIds = _subscriptionProvider.GetCurrentTokenIds();

            _logger.LogInformation(
                "{Message}. Version={Version}, Markets={MarketCount}, TokenIds={TokenCount}, StreamId={StreamId}",
                message,
                version,
                subscriptions.Count,
                tokenIds.Count,
                streamId);

            LogPreview(subscriptions);
        }

        private void LogPreview(IReadOnlyCollection<FootballMarketSubscriptionRequest> subscriptions)
        {
            foreach (var item in subscriptions.Take(5))
            {
                _logger.LogInformation(
                    "Football subscription ready. ConditionId={ConditionId}, YesTokenId={YesTokenId}, NoTokenId={NoTokenId}, Question={Question}",
                    item.ConditionId,
                    item.YesTokenId,
                    item.NoTokenId,
                    item.Question);
            }
        }
    }
}
