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

            // Redis — conversão de URL feita dentro de RedisConnectionFactory
            services.AddSingleton<RedisConnectionFactory>();
            services.AddSingleton<IStreamPublisher, RedisStreamPublisher>();
            services.AddSingleton<IStreamConsumer, RedisStreamConsumer>();

            // Reutiliza a conexão do RedisConnectionFactory para evitar duas conexões abertas
            services.AddSingleton<IConnectionMultiplexer>(sp =>
            {
                var factory = sp.GetRequiredService<RedisConnectionFactory>();
                return factory.Connection;
            });

            // Postgres — converte URL do Railway para formato Npgsql
            services.AddSingleton<NpgsqlConnectionFactory>(sp =>
            {
                var rawPostgres = config["Postgres:Connection"];
                var rawConnectionString = !string.IsNullOrWhiteSpace(rawPostgres) ? rawPostgres
                    : config["Postgres:ConnectionString"] is string cs && !string.IsNullOrWhiteSpace(cs) ? cs
                    : Environment.GetEnvironmentVariable("DATABASE_URL")
                    ?? "Host=localhost;Port=5432;Database=arb;Username=postgres;Password=1234";

                var connectionString = ConvertPostgresUrl(rawConnectionString);

                return new NpgsqlConnectionFactory(
                    Options.Create(new PostgresOptions { Connection = connectionString }));
            });

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

            // Soccer Catalog Redis — conversão de URL feita dentro de FootballCatalogRedisConnectionFactory
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

        /// <summary>
        /// Converte postgresql://user:pass@host:port/db para o formato Npgsql key-value.
        /// Retorna sem alteração se já estiver no formato correto.
        /// </summary>
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
        private static string? NullIfEmpty(this string? s) =>
            string.IsNullOrWhiteSpace(s) ? null : s;
    }
}