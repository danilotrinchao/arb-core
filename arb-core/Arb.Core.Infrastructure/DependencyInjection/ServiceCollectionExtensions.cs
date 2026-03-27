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
            // Lê Redis:Connection (appsettings local) ou Redis__Connection (variável de ambiente Railway)
            // Suporta formato redis://user:password@host:port (Railway) e host:port (local)
            services.AddSingleton<RedisConnectionFactory>();
            services.AddSingleton<IStreamPublisher, RedisStreamPublisher>();
            services.AddSingleton<IStreamConsumer, RedisStreamConsumer>();

            services.AddSingleton<IConnectionMultiplexer>(_ =>
            {
                var rawUrl =
                    config["Redis:Connection"] ??
                    config["Redis:ConnectionString"] ??
                    config["Redis:Configuration"] ??
                    "localhost:6379";

                var connectionString = ConvertRedisUrl(rawUrl);

                return ConnectionMultiplexer.Connect(connectionString);
            });

            // Postgres
            // Lê Postgres:Connection (appsettings local) ou Postgres__Connection (variável de ambiente Railway)
            // Npgsql aceita tanto o formato postgresql://user:pass@host:port/db (Railway)
            // quanto o formato Host=...;Port=...;Database=... (local)
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

        /// <summary>
        /// Converte URL no formato Redis do Railway (redis://user:password@host:port)
        /// para o formato esperado pelo StackExchange.Redis (host:port,password=senha).
        /// Se a URL já estiver no formato correto, retorna sem alteração.
        /// </summary>
        private static string ConvertRedisUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return "localhost:6379";

            // Já está no formato StackExchange.Redis — retorna direto
            if (!url.StartsWith("redis://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("rediss://", StringComparison.OrdinalIgnoreCase))
                return url;

            try
            {
                var uri = new Uri(url);
                var host = uri.Host;
                var port = uri.Port > 0 ? uri.Port : 6379;

                // UserInfo pode ser "default:password" ou apenas "password"
                var password = string.Empty;
                if (!string.IsNullOrWhiteSpace(uri.UserInfo))
                {
                    password = uri.UserInfo.Contains(':')
                        ? uri.UserInfo.Split(':', 2)[1]
                        : uri.UserInfo;
                }

                // rediss:// indica TLS
                var tls = url.StartsWith("rediss://", StringComparison.OrdinalIgnoreCase)
                    ? ",ssl=true,abortConnect=false"
                    : ",abortConnect=false";

                return string.IsNullOrWhiteSpace(password)
                    ? $"{host}:{port}{tls}"
                    : $"{host}:{port},password={password}{tls}";
            }
            catch
            {
                // Se a conversão falhar por qualquer motivo, retorna a URL original
                // e deixa o StackExchange.Redis tentar interpretar diretamente
                return url;
            }
        }
    }
}
