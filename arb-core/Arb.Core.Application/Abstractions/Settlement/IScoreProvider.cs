using Arb.Core.Application.Request;

namespace Arb.Core.Application.Abstractions.Settlement
{
    public interface IScoreProvider
    {
        Task<IReadOnlyList<EventScoreSnapshot>> FetchScoresAsync(
            string sportKey,
            IReadOnlyList<string> eventKeys,
            CancellationToken ct);
    }
}
