using Arb.Core.Application.Abstractions.Messaging;
using StackExchange.Redis;

namespace Arb.Core.Infrastructure.Redis
{
    public sealed class RedisStreamConsumer : IStreamConsumer
    {
        private readonly RedisConnectionFactory _factory;

        public RedisStreamConsumer(RedisConnectionFactory factory)
        {
            _factory = factory;
        }
        public async Task AckAsync(string stream, string groupName, string messageId, CancellationToken ct)
        {
            var db = _factory.Connection.GetDatabase();

            for (var attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    await db.StreamAcknowledgeAsync(stream, groupName, messageId);
                    return;
                }
                catch (RedisTimeoutException ex) when (attempt < 3)
                {
                   // _logger.LogWarning(
                   //     ex,
                   //     "Redis ACK timeout. stream={Stream} group={Group} msgId={MessageId} attempt={Attempt}",
                   //     stream, groupName, messageId, attempt);

                    await Task.Delay(500 * attempt, ct);
                }
            }

            // última tentativa sem swallow silencioso
            await db.StreamAcknowledgeAsync(stream, groupName, messageId);
        }
        public async Task EnsureConsumerGroupAsync(string stream, string groupName, CancellationToken ct)
        {
            var db = _factory.Connection.GetDatabase();

            try
            {
                // "$" => começa do "novo" (não reprocessa histórico). Para replay, você muda isso depois.
                await db.StreamCreateConsumerGroupAsync(stream, groupName, "$", createStream: true)
                    .ConfigureAwait(false);
            }
            catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP", StringComparison.OrdinalIgnoreCase))
            {
                // group já existe
            }
        }

        public async Task<IReadOnlyList<StreamMessage>> ReadGroupAsync(
            string stream,
            string groupName,
            string consumerName,
            int count,
            TimeSpan block,
            CancellationToken ct)
        {
            var db = _factory.Connection.GetDatabase();

            // ">" => novas mensagens para o grupo
            var entries = await db.StreamReadGroupAsync(stream, groupName, consumerName, ">", count)
                .ConfigureAwait(false);

            // Observação: StackExchange.Redis não oferece "block" no StreamReadGroupAsync.
            // Nesta etapa, usamos polling com Delay no Worker. (Simples e funciona.)
            return entries.Select(e =>
                    new StreamMessage(
                        e.Id,
                        e.Values.ToDictionary(v => v.Name.ToString(), v => v.Value.ToString())))
                .ToList();
        }

        //public async Task AckAsync(string stream, string groupName, string messageId, CancellationToken ct)
        //{
        //    var db = _factory.Connection.GetDatabase();
        //    await db.StreamAcknowledgeAsync(stream, groupName, messageId).ConfigureAwait(false);
        //}
    }
}
