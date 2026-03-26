using Arb.Core.Application.Abstractions.MarketData;
using Arb.Core.Application.Abstractions.Messaging;
using Arb.Core.Application.Abstractions.Persistence;
using Arb.Core.Application.Abstractions.Settlement;
using Arb.Core.Application.Abstractions.Signals;
using Arb.Core.Application.UseCases.MarketData;
using Arb.Core.Application.UseCases.Signals;
using Arb.Core.Infrastructure.External.TheOddsApi;
using Arb.Core.Infrastructure.Normalization;
using Arb.Core.Infrastructure.Policies;
using Arb.Core.Infrastructure.Postgres;
using Arb.Core.Infrastructure.Redis;
using Arb.Core.Infrastructure.Redis.SoccerCatalog;
using Arb.Core.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Arb.Core.Infrastructure.DependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddArbInfrastructure(
        this IServiceCollection services,
        IConfiguration config)
        {
            // Options
            services.Configure<RedisOptions>(config.GetSection(RedisOptions.SectionName));
            services.Configure<PostgresOptions>(config.GetSection(PostgresOptions.SectionName));
            services.Configure<StreamsOptions>(config.GetSection(StreamsOptions.SectionName));
            services.Configure<PolymarketObservationOptions>(
                config.GetSection(PolymarketObservationOptions.SectionName));
            services.Configure<PolymarketObservedSignalOptions>(
                config.GetSection(PolymarketObservedSignalOptions.SectionName));

            // Redis geral
            services.AddSingleton<RedisConnectionFactory>();
            services.AddSingleton<IStreamPublisher, RedisStreamPublisher>();
            services.AddSingleton<IStreamConsumer, RedisStreamConsumer>();

            services.AddSingleton<IConnectionMultiplexer>(_ =>
            {
                var connectionString =
                    config["Redis:Configuration"] ??
                    config["Redis:ConnectionString"] ??
                    "localhost:6379";

                return ConnectionMultiplexer.Connect(connectionString);
            });

            // Postgres
            services.AddSingleton<NpgsqlConnectionFactory>();
            services.AddSingleton<DbInitializer>();
            services.AddScoped<IOrderIntentRepository, OrderIntentRepository>();
            services.AddScoped<IExecutionReportRepository, ExecutionReportRepository>();
            services.AddScoped<IPortfolioRepository, PortfolioRepository>();
            services.AddScoped<IPositionRepository, PositionRepository>();

            // The Odds API
            services.Configure<TheOddsApiOptions>(config.GetSection(TheOddsApiOptions.SectionName));

            services.AddHttpClient<TheOddsApiClient>((sp, client) =>
            {
                var options = sp
                    .GetRequiredService<Microsoft.Extensions.Options.IOptions<TheOddsApiOptions>>()
                    .Value;

                client.BaseAddress = new Uri(options.BaseUrl);
                client.Timeout = TimeSpan.FromSeconds(60);
            });

            services.AddScoped<IMarketOddsProvider, TheOddsApiProvider>();
            services.AddScoped<IScoreProvider, TheOddsApiScoreProvider>();

            // Normalization / Polling / Budget / Dedup
            services.AddSingleton<IOddsNormalizer, OddsTickNormalizer>();
            services.AddSingleton<IMarketPollingPolicy, AdaptivePollingPolicy>();
            services.AddSingleton<CreditBudgetService>();
            services.AddSingleton<SnapshotDedupService>();

            // Soccer Catalog - Redis
            services.Configure<FootballCatalogRedisOptions>(
                config.GetSection(FootballCatalogRedisOptions.SectionName));

            services.AddSingleton<FootballCatalogRedisConnectionFactory>();
            services.AddSingleton<IFootballCatalogRedisRepository, FootballCatalogRedisRepository>();
            services.AddSingleton<IFootballMarketRegistry, FootballMarketRegistry>();
            services.AddSingleton<IFootballMarketSubscriptionProvider, FootballMarketSubscriptionProvider>();
            services.AddSingleton<IObservedSoccerToPolymarketProjector, ObservedSoccerToPolymarketProjector>();
            services.AddSingleton<RefreshFootballCatalogSnapshotUseCase>();

            // Polymarket observed signal
            services.AddSingleton<IPolymarketObservedSignalEngine, PolymarketObservedSignalEngine>();

            return services;
        }
    }
}
