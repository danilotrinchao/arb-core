namespace Arb.Core.Application.Abstractions.Messaging
{
    public interface IStreamPublisher
    {
        Task PublishAsync(string streamName, IReadOnlyDictionary<string, string> fields, CancellationToken ct);
    }
}
