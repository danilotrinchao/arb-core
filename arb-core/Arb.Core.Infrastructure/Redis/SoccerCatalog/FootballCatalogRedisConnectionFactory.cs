using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Arb.Core.Infrastructure.Redis.SoccerCatalog
{
    public class FootballCatalogRedisConnectionFactory : IDisposable
    {
        private readonly Lazy<ConnectionMultiplexer> _lazyConnection;

        public FootballCatalogRedisConnectionFactory(
            IOptions<FootballCatalogRedisOptions> options)
        {
            var value = options.Value;

            _lazyConnection = new Lazy<ConnectionMultiplexer>(() =>
            {
                // Converte redis:// (Railway) para o formato aceito pelo StackExchange.Redis
                var converted = ConvertRedisUrl(value.ConnectionString);

                var configuration = ConfigurationOptions.Parse(converted);
                configuration.AbortOnConnectFail = false;
                configuration.ConnectRetry = 3;
                configuration.ConnectTimeout = value.ConnectTimeoutMs;
                configuration.SyncTimeout = value.SyncTimeoutMs;
                configuration.AsyncTimeout = value.AsyncTimeoutMs;
                configuration.KeepAlive = value.KeepAliveSeconds;

                return ConnectionMultiplexer.Connect(configuration);
            });
        }

        public IConnectionMultiplexer GetConnection() => _lazyConnection.Value;

        public IDatabase GetDatabase() => _lazyConnection.Value.GetDatabase();

        public void Dispose()
        {
            if (_lazyConnection.IsValueCreated)
                _lazyConnection.Value.Dispose();
        }

        private static string ConvertRedisUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return "localhost:6379";

            if (!url.StartsWith("redis://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("rediss://", StringComparison.OrdinalIgnoreCase))
                return url;

            try
            {
                var isTls = url.StartsWith("rediss://", StringComparison.OrdinalIgnoreCase);

                var withoutScheme = isTls
                    ? url["rediss://".Length..]
                    : url["redis://".Length..];

                var atIndex = withoutScheme.LastIndexOf('@');
                var credentials = atIndex >= 0 ? withoutScheme[..atIndex] : string.Empty;
                var hostPart = atIndex >= 0 ? withoutScheme[(atIndex + 1)..] : withoutScheme;

                var slashIndex = hostPart.IndexOf('/');
                if (slashIndex >= 0)
                    hostPart = hostPart[..slashIndex];

                var hostSegments = hostPart.Split(':');
                var host = hostSegments[0];
                var port = hostSegments.Length >= 2
                    ? int.TryParse(hostSegments[1], out var p) ? p : 6379
                    : 6379;

                var password = string.Empty;
                if (!string.IsNullOrWhiteSpace(credentials))
                {
                    var colonIndex = credentials.IndexOf(':');
                    password = colonIndex >= 0
                        ? credentials[(colonIndex + 1)..]
                        : credentials;
                }

                var tlsSuffix = isTls ? ",ssl=true" : string.Empty;

                return string.IsNullOrWhiteSpace(password)
                    ? $"{host}:{port},abortConnect=false{tlsSuffix}"
                    : $"{host}:{port},password={password},abortConnect=false{tlsSuffix}";
            }
            catch
            {
                return url;
            }
        }
    }
}