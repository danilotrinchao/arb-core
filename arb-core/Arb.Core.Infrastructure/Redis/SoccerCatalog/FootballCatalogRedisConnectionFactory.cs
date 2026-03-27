using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Arb.Core.Infrastructure.Redis.SoccerCatalog
{
    public sealed class FootballCatalogRedisConnectionFactory : IDisposable
    {
        private readonly Lazy<ConnectionMultiplexer> _lazyConnection;

        public FootballCatalogRedisConnectionFactory(
            IOptions<FootballCatalogRedisOptions> options)
        {
            var value = options.Value;

            _lazyConnection = new Lazy<ConnectionMultiplexer>(() =>
            {
                var configuration = ConfigurationOptions.Parse(value.ConnectionString);

                configuration.AbortOnConnectFail = false;
                configuration.ConnectRetry = 3;
                configuration.ConnectTimeout = value.ConnectTimeoutMs;
                configuration.SyncTimeout = value.SyncTimeoutMs;
                configuration.AsyncTimeout = value.AsyncTimeoutMs;
                configuration.KeepAlive = value.KeepAliveSeconds;

                return ConnectionMultiplexer.Connect(configuration);
            });
        }

        public IConnectionMultiplexer GetConnection()
        {
            return _lazyConnection.Value;
        }

        public IDatabase GetDatabase()
        {
            return _lazyConnection.Value.GetDatabase();
        }

        public void Dispose()
        {
            if (_lazyConnection.IsValueCreated)
            {
                _lazyConnection.Value.Dispose();
            }
        }
    }
}
