namespace Arb.Core.Application.Abstractions.Persistence
{
    public interface IExecutionRequestRepository
    {
        Task InsertAsync(ExecutionRequestRecord request, CancellationToken ct);
        Task MarkDispatchedAsync(Guid id, DateTime sentAt, CancellationToken ct);
        Task MarkAcceptedAsync(Guid id, string externalOrderId, string? rawResponse, DateTime updatedAt, CancellationToken ct);
        Task MarkRejectedAsync(Guid id, string? errorCode, string? errorMessage, string? rawResponse, DateTime updatedAt, CancellationToken ct);
        Task MarkFailedAsync(Guid id, string? errorCode, string? errorMessage, string? rawResponse, DateTime updatedAt, CancellationToken ct);

        Task<IReadOnlyList<ExecutionRequestRecord>> ListPendingReconciliationAsync(
            int maxCount,
            CancellationToken ct);

        Task MarkPartiallyFilledAsync(
            Guid id,
            string? rawResponse,
            DateTime updatedAt,
            CancellationToken ct);

        Task MarkFilledAsync(
            Guid id,
            string? rawResponse,
            DateTime updatedAt,
            CancellationToken ct);

        Task MarkCancelledAsync(
            Guid id,
            string? rawResponse,
            DateTime updatedAt,
            CancellationToken ct);

        Task MarkExpiredAsync(
            Guid id,
            string? rawResponse,
            DateTime updatedAt,
            CancellationToken ct);

        Task MarkMaterializedAsync(
            Guid requestId,
            string materializedPositionId,
            DateTime materializedAt,
            DateTime lastReconciliationAt,
            CancellationToken ct);

        Task MarkReconciledAsync(
            Guid requestId,
            DateTime lastReconciliationAt,
            CancellationToken ct);
    }

    public class ExecutionRequestRecord
    {
        public Guid Id { get; set; }
        public Guid? IntentId { get; set; }
        public string? PositionId { get; set; }
        public string Action { get; set; } = string.Empty;
        public string TokenId { get; set; } = string.Empty;
        public string? MarketConditionId { get; set; }
        public string Side { get; set; } = string.Empty;
        public double LimitPrice { get; set; }
        public double RequestedSizeUsd { get; set; }
        public string Status { get; set; } = string.Empty;
        public string CorrelationId { get; set; } = string.Empty;
        public string? ExternalOrderId { get; set; }
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? SentAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string RawRequest { get; set; } = string.Empty;
        public string? RawResponse { get; set; }
        public string? MaterializedPositionId { get; set; }
        public DateTime? MaterializedAt { get; set; }
        public DateTime? LastReconciliationAt { get; set; }
    }
}