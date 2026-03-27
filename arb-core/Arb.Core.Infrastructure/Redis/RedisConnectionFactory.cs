using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Arb.Core.Infrastructure.Redis
{
    public sealed class RedisConnectionFactory : IDisposable
    {
        private readonly Lazy<ConnectionMultiplexer> _lazyConn;

        public RedisConnectionFactory(IOptions<RedisOptions> options)
        {
            var configuration = ConfigurationOptions.Parse(options.Value.Connection);
            configuration.AbortOnConnectFail = false;
            configuration.ConnectRetry = 3;
            configuration.ConnectTimeout = 10000;
            configuration.SyncTimeout = 30000;
            configuration.AsyncTimeout = 30000;
            configuration.KeepAlive = 60;

            _lazyConn = new Lazy<ConnectionMultiplexer>(() => ConnectionMultiplexer.Connect(configuration));
        }

        public IConnectionMultiplexer Connection => _lazyConn.Value;

        public void Dispose()
        {
            if (_lazyConn.IsValueCreated)
                _lazyConn.Value.Dispose();
        }
    }
}
