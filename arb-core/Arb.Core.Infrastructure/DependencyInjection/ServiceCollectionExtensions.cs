using Arb.Core.Application.Abstractions.Execution;
using Arb.Core.Application.Abstractions.MarketData;
using Arb.Core.Application.Abstractions.Messaging;
using Arb.Core.Application.Abstractions.Persistence;
using Arb.Core.Application.Abstractions.Settlement;
using Arb.Core.Application.Abstractions.Signals;
using Arb.Core.Application.Services;
using Arb.Core.Application.UseCases.MarketData;
using Arb.Core.Application.UseCases.Signals;
using Arb.Core.Infrastructure.External.Execution;
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
        /// <summary>
        /// Looks up an environment variable by name, also checking for keys with trailing
        /// whitespace — a known Railway dashboard issue where variable names can be saved
        /// with accidental trailing spaces, preventing normal config binding.
        /// </summary>
        private static string? GetEnvVar(string name)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (value is not null)
                return value;

            // Scan all env vars and match after trimming the key, to handle trailing-space names.
            foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            {
                if (entry.Key is string key && key.Trim() == name)
                    return entry.Value?.ToString();
            }

            return null;
        }

        public static IServiceCollection AddArbInfrastructure(
            this IServiceCollection services,
            IConfiguration config)
        {
            // Resolve connection strings early, tolerating env var names with trailing spaces.
            var postgresConnection =
                GetEnvVar("Postgres__Connection") ??
                GetEnvVar("Postgres__ConnectionString") ??
                config["Postgres:Connection"] ??
                config["Postgres:ConnectionString"];

            var redisConnection =
                GetEnvVar("Redis__Connection") ??
                GetEnvVar("Redis__ConnectionString") ??
                config["Redis:Connection"] ??
                config["Redis:ConnectionString"];

            var footballCatalogRedisConnection =
                GetEnvVar("FootballCatalogRedis__ConnectionString") ??
                GetEnvVar("FootballCatalogRedis__Connection") ??
                config["FootballCatalogRedis:ConnectionString"] ??
                config["FootballCatalogRedis:Connection"];

            services.Configure<RedisOptions>(opts =>
            {
                var section = config.GetSection(RedisOptions.SectionName);
                section.Bind(opts);
                if (!string.IsNullOrWhiteSpace(redisConnection))
                    opts.Connection = redisConnection;
            });
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
                    postgresConnection ??
                    "Host=postgres.railway.internal;Port=5432;Database=railway;Username=postgres;Password=WfxqHNUYYncGjfOASkSBRAvByFSSoEdE";

                var connectionString = ConvertPostgresUrl(rawConnectionString);

                return new NpgsqlConnectionFactory(
                    Options.Create(new PostgresOptions { Connection = connectionString }));
            });

            services.AddSingleton<DbInitializer>();
            services.AddScoped<IExecutionRequestRepository, ExecutionRequestRepository>();
            services.AddScoped<IExecutionFillRepository, ExecutionFillRepository>();
            services.AddScoped<IOrderIntentRepository, OrderIntentRepository>();
            services.AddScoped<IOrderIntentRejectionRepository, OrderIntentRejectionRepository>();
            services.AddScoped<IExecutionReportRepository, ExecutionReportRepository>();
            services.AddScoped<IPortfolioRepository, PortfolioRepository>();
            services.AddScoped<IPositionRepository, PositionRepository>();
            services.AddScoped<IPositionAnalyticsRepository, PositionAnalyticsRepository>();
            services.AddScoped<ITokenHealthRepository, TokenHealthRepository>();
            services.Configure<TheOddsApiOptions>(config.GetSection(TheOddsApiOptions.SectionName));

            services.AddHttpClient<TheOddsApiClient>((sp, client) =>
            {
                var options = sp
                    .GetRequiredService<IOptions<TheOddsApiOptions>>()
                    .Value;

                client.BaseAddress = new Uri(options.BaseUrl);
                client.Timeout = TimeSpan.FromSeconds(120);
            });
            var executionAdapterBaseUrl = config["ExecutionAdapter:BaseUrl"];

            if (string.IsNullOrWhiteSpace(executionAdapterBaseUrl))
                throw new InvalidOperationException("ExecutionAdapter:BaseUrl was not configured.");

            if (!Uri.TryCreate(executionAdapterBaseUrl, UriKind.Absolute, out var executionAdapterUri))
                throw new InvalidOperationException(
                    $"ExecutionAdapter:BaseUrl is invalid: '{executionAdapterBaseUrl}'.");

            //services.AddHttpClient<IExecutionGateway, HttpExecutionGateway>(client =>
            //{
            //    client.BaseAddress = executionAdapterUri;
            //    client.Timeout = TimeSpan.FromSeconds(15);
            //});
            services.AddScoped<IMarketOddsProvider, TheOddsApiProvider>();
            services.AddScoped<IScoreProvider, TheOddsApiScoreProvider>();

            services.AddSingleton<IOddsNormalizer, OddsTickNormalizer>();
            services.AddSingleton<IMarketPollingPolicy, AdaptivePollingPolicy>();
            services.AddSingleton<CreditBudgetService>();
            services.AddSingleton<SnapshotDedupService>();

            services.Configure<FootballCatalogRedisOptions>(opts =>
            {
                var section = config.GetSection(FootballCatalogRedisOptions.SectionName);
                section.Bind(opts);
                if (!string.IsNullOrWhiteSpace(footballCatalogRedisConnection))
                    opts.ConnectionString = footballCatalogRedisConnection;
            });

            services.AddSingleton<FootballCatalogRedisConnectionFactory>();
            services.AddSingleton<IFootballCatalogRedisRepository, FootballCatalogRedisRepository>();
            services.AddSingleton<IFootballMarketRegistry, FootballMarketRegistry>();
            services.AddSingleton<IFootballMarketSubscriptionProvider, FootballMarketSubscriptionProvider>();
            services.AddSingleton<IObservedSoccerToPolymarketProjector, ObservedSoccerToPolymarketProjector>();
            services.AddSingleton<RefreshFootballCatalogSnapshotUseCase>();

            services.AddSingleton<IPolymarketObservedSignalEngine, PolymarketObservedSignalEngine>();
            services.AddScoped<IExecutionDispatchService, ExecutionDispatchService>();

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