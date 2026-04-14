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
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Arb.Core.Infrastructure.DependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddArbInfrastructure(
            this IServiceCollection services,
            IConfiguration config)
        {
            services.Configure<RedisOptions>(config.GetSection(RedisOptions.SectionName));
            services.Configure<PostgresOptions>(config.GetSection(PostgresOptions.SectionName));
            services.Configure<StreamsOptions>(config.GetSection(StreamsOptions.SectionName));
            services.Configure<PolymarketObservationOptions>(
                config.GetSection(PolymarketObservationOptions.SectionName));
            services.Configure<PolymarketObservedSignalOptions>(
                config.GetSection(PolymarketObservedSignalOptions.SectionName));

            services.AddSingleton<RefreshFootballCatalogSnapshotUseCase>();
            services.AddSingleton<RefreshNbaCatalogSnapshotUseCase>();

            services.AddSingleton<IPolymarketObservedSignalEngine, PolymarketObservedSignalEngine>();

            services.AddSingleton<RedisConnectionFactory>();
            services.AddSingleton<IStreamPublisher, RedisStreamPublisher>();
            services.AddSingleton<IStreamConsumer, RedisStreamConsumer>();

            services.AddSingleton<IConnectionMultiplexer>(sp =>
            {
                var factory = sp.GetRequiredService<RedisConnectionFactory>();
                return factory.Connection;
            });

            services.AddSingleton<NpgsqlConnectionFactory>(sp =>
            {
                var rawConnectionString =
                    config["Postgres:Connection"] ??
                    config["Postgres:ConnectionString"] ??
                    "Host=postgres.railway.internal;Port=5432;Database=railway;Username=postgres;Password=WfxqHNUYYncGjfOASkSBRAvByFSSoEdE";

                var connectionString = ConvertPostgresUrl(rawConnectionString);

                return new NpgsqlConnectionFactory(
                    Options.Create(new PostgresOptions { Connection = connectionString }));
            });

            services.AddSingleton<DbInitializer>();

            services.AddScoped<IOrderIntentRepository, OrderIntentRepository>();
            services.AddScoped<IOrderIntentRejectionRepository, OrderIntentRejectionRepository>();
            services.AddScoped<IExecutionReportRepository, ExecutionReportRepository>();
            services.AddScoped<IPortfolioRepository, PortfolioRepository>();
            services.AddScoped<IPositionRepository, PositionRepository>();
            services.AddScoped<IPositionAnalyticsRepository, PositionAnalyticsRepository>();

            services.Configure<TheOddsApiOptions>(config.GetSection(TheOddsApiOptions.SectionName));

            services.AddHttpClient<TheOddsApiClient>((sp, client) =>
            {
                var options = sp
                    .GetRequiredService<IOptions<TheOddsApiOptions>>()
                    .Value;

                client.BaseAddress = new Uri(options.BaseUrl);
                client.Timeout = TimeSpan.FromSeconds(120);
            });

            services.AddScoped<IMarketOddsProvider, TheOddsApiProvider>();
            services.AddScoped<IScoreProvider, TheOddsApiScoreProvider>();

            services.AddSingleton<IOddsNormalizer, OddsTickNormalizer>();
            services.AddSingleton<IMarketPollingPolicy, AdaptivePollingPolicy>();
            services.AddSingleton<CreditBudgetService>();
            services.AddSingleton<SnapshotDedupService>();

            services.Configure<FootballCatalogRedisOptions>(
                config.GetSection(FootballCatalogRedisOptions.SectionName));

            services.AddSingleton<FootballCatalogRedisConnectionFactory>();
            services.AddSingleton<IFootballCatalogRedisRepository, FootballCatalogRedisRepository>();
            services.AddSingleton<IFootballMarketRegistry, FootballMarketRegistry>();
            services.AddSingleton<IFootballMarketSubscriptionProvider, FootballMarketSubscriptionProvider>();
            services.AddSingleton<IObservedSoccerToPolymarketProjector, ObservedSoccerToPolymarketProjector>();
            services.AddSingleton<RefreshFootballCatalogSnapshotUseCase>();

            services.AddSingleton<IPolymarketObservedSignalEngine, PolymarketObservedSignalEngine>();

            return services;
        }

        private static string ConvertPostgresUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return url;

            if (!url.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase))
                return url;

            try
            {
                var uri = new Uri(url);
                var host = uri.Host;
                var port = uri.Port > 0 ? uri.Port : 5432;
                var database = uri.AbsolutePath.TrimStart('/');

                var username = string.Empty;
                var password = string.Empty;

                if (!string.IsNullOrWhiteSpace(uri.UserInfo))
                {
                    var parts = uri.UserInfo.Split(':', 2);
                    username = parts[0];
                    password = parts.Length > 1 ? parts[1] : string.Empty;
                }

                return $"Host={host};Port={port};Database={database};" +
                       $"Username={username};Password={password};" +
                       $"SSL Mode=Require;Trust Server Certificate=true";
            }
            catch
            {
                return url;
            }
        }
    }
}