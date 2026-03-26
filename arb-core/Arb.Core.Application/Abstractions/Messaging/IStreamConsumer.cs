namespace Arb.Core.Application.Abstractions.Messaging
{
    public interface IStreamConsumer
    {
        Task EnsureConsumerGroupAsync(string stream, string groupName, CancellationToken ct);

        Task<IReadOnlyList<StreamMessage>> ReadGroupAsync(
            string stream,
            string groupName,
            string consumerName,
            int count,
            TimeSpan block,
            CancellationToken ct);

        Task AckAsync(string stream, string groupName, string messageId, CancellationToken ct);
    }

    public sealed record StreamMessage(string Id, IReadOnlyDictionary<string, string> Fields);
}
