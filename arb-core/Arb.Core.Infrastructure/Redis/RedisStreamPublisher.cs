using Arb.Core.Application.Abstractions.Messaging;
using StackExchange.Redis;

namespace Arb.Core.Infrastructure.Redis
{
    public sealed class RedisStreamPublisher : IStreamPublisher
    {
        private readonly RedisConnectionFactory _factory;

        public RedisStreamPublisher(RedisConnectionFactory factory)
        {
            _factory = factory;
        }

        public async Task PublishAsync(string streamName, IReadOnlyDictionary<string, string> fields, CancellationToken ct)
        {
            // StackExchange.Redis não usa CancellationToken em todos métodos; ok nesta etapa.
            var db = _factory.Connection.GetDatabase();

            var entries = fields.Select(kv => new NameValueEntry(kv.Key, kv.Value)).ToArray();
            await db.StreamAddAsync(streamName, entries).ConfigureAwait(false);
        }
    }
}
