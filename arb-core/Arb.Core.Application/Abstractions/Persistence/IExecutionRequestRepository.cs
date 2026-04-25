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

    public record ExecutionRequestRecord(
     Guid Id,
     Guid? IntentId,
     string? PositionId,
     string Action,
     string TokenId,
     string? MarketConditionId,
     string Side,
     double LimitPrice,
     double RequestedSizeUsd,
     string Status,
     string CorrelationId,
     string? ExternalOrderId,
     string? ErrorCode,
     string? ErrorMessage,
     DateTime CreatedAt,
     DateTime? SentAt,
     DateTime UpdatedAt,
     string RawRequest,
     string? RawResponse,
     string? MaterializedPositionId,
     DateTime? MaterializedAt,
     DateTime? LastReconciliationAt
     );
}