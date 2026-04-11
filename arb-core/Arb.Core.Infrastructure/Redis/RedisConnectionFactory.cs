using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Arb.Core.Infrastructure.Redis
{
    public class RedisConnectionFactory : IDisposable
    {
        private readonly Lazy<ConnectionMultiplexer> _lazyConn;

        public RedisConnectionFactory(IOptions<RedisOptions> options)
        {
            var rawConnection = options.Value.Connection;
            var converted = ConvertRedisUrl(rawConnection);

            var configuration = ConfigurationOptions.Parse(converted);
            configuration.AbortOnConnectFail = false;
            configuration.ConnectRetry = 3;
            configuration.ConnectTimeout = 10000;
            configuration.SyncTimeout = 30000;
            configuration.AsyncTimeout = 30000;
            configuration.KeepAlive = 120;

            _lazyConn = new Lazy<ConnectionMultiplexer>(
                () => ConnectionMultiplexer.Connect(configuration));
        }

        public IConnectionMultiplexer Connection => _lazyConn.Value;

        public void Dispose()
        {
            if (_lazyConn.IsValueCreated)
                _lazyConn.Value.Dispose();
        }

        private static string ConvertRedisUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return "redis.railway.internal:6379,abortConnect=false";

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

                // Railway às vezes entrega host:59446:6379 — usa só a primeira porta
                var hostSegments = hostPart.Split(':');
                var portStr = hostSegments.Length >= 2 ? hostSegments[^1] : "6379";
                var port = int.TryParse(portStr, out var p) ? p : 6379;
                var host = hostSegments[0];

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