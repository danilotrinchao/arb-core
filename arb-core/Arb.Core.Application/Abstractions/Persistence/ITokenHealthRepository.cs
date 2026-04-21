namespace Arb.Core.Application.Abstractions.Persistence
{
    public interface ITokenHealthRepository
    {
        Task<TokenHealthState?> GetAsync(string tokenId, CancellationToken ct);

        Task<bool> IsBlockedAsync(
            string tokenId,
            DateTime utcNow,
            CancellationToken ct);

        Task UpsertNoOrderbookAsync(
            string tokenId,
            int httpStatus,
            string? responseBody,
            DateTime utcNow,
            DateTime retryAfter,
            CancellationToken ct);

        Task MarkHealthyAsync(
            string tokenId,
            DateTime utcNow,
            CancellationToken ct);
    }

    public sealed class TokenHealthState
    {
        public string TokenId { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string Reason { get; init; } = string.Empty;
        public int FailureCount { get; init; }
        public DateTime FirstSeenAt { get; init; }
        public DateTime LastSeenAt { get; init; }
        public DateTime? RetryAfter { get; init; }
        public int? LastHttpStatus { get; init; }
        public string? LastResponseBody { get; init; }
    }
}