using Arb.Core.Application.Abstractions.Persistence;
using Dapper;

namespace Arb.Core.Infrastructure.Postgres
{
    public class ExecutionRequestRepository : IExecutionRequestRepository
    {
        private readonly NpgsqlConnectionFactory _factory;

        public ExecutionRequestRepository(NpgsqlConnectionFactory factory)
        {
            _factory = factory;
        }

        public async Task InsertAsync(ExecutionRequestRecord request, CancellationToken ct)
        {
            await using var conn = _factory.Create();
            await conn.OpenAsync(ct);

            const string sql = """
                                INSERT INTO execution_requests
                                (
                                    id,
                                    intent_id,
                                    position_id,
                                    action,
                                    token_id,
                                    market_condition_id,
                                    side,
                                    limit_price,
                                    requested_size_usd,
                                    status,
                                    correlation_id,
                                    external_order_id,
                                    error_code,
                                    error_message,
                                    created_at,
                                    sent_at,
                                    updated_at,
                                    raw_request,
                                    raw_response,
                                    materialized_position_id,
                                    materialized_at,
                                    last_reconciliation_at
                                )
                                VALUES
                                (
                                    @Id,
                                    @IntentId,
                                    @PositionId,
                                    @Action,
                                    @TokenId,
                                    @MarketConditionId,
                                    @Side,
                                    @LimitPrice,
                                    @RequestedSizeUsd,
                                    @Status,
                                    @CorrelationId,
                                    @ExternalOrderId,
                                    @ErrorCode,
                                    @ErrorMessage,
                                    @CreatedAt,
                                    @SentAt,
                                    @UpdatedAt,
                                    @RawRequest::jsonb,
                                    @RawResponse::jsonb,
                                    @MaterializedPositionId,
                                    @MaterializedAt,
                                    @LastReconciliationAt
                                );
                                """;

            await conn.ExecuteAsync(
                new CommandDefinition(sql, new
                {
                    request.Id,
                    request.IntentId,
                    request.PositionId,
                    request.Action,
                    request.TokenId,
                    request.MarketConditionId,
                    request.Side,
                    request.LimitPrice,
                    request.RequestedSizeUsd,
                    request.Status,
                    request.CorrelationId,
                    request.ExternalOrderId,
                    request.ErrorCode,
                    request.ErrorMessage,
                    request.CreatedAt,
                    request.SentAt,
                    request.UpdatedAt,
                    request.RawRequest,
                    request.RawResponse,
                    request.MaterializedPositionId,
                    request.MaterializedAt,
                    request.LastReconciliationAt
                }, cancellationToken: ct));
        }

        public async Task MarkDispatchedAsync(Guid id, DateTime sentAt, CancellationToken ct)
        {
            await UpdateStatusAsync(
                id: id,
                status: "DISPATCHED",
                externalOrderId: null,
                errorCode: null,
                errorMessage: null,
                rawResponse: null,
                sentAt: sentAt,
                updatedAt: sentAt,
                ct: ct);
        }

        public async Task MarkAcceptedAsync(Guid id, string externalOrderId, string? rawResponse, DateTime updatedAt, CancellationToken ct)
        {
            await UpdateStatusAsync(
                id: id,
                status: "ACCEPTED",
                externalOrderId: externalOrderId,
                errorCode: null,
                errorMessage: null,
                rawResponse: rawResponse,
                sentAt: null,
                updatedAt: updatedAt,
                ct: ct);
        }

        public async Task MarkRejectedAsync(Guid id, string? errorCode, string? errorMessage, string? rawResponse, DateTime updatedAt, CancellationToken ct)
        {
            await UpdateStatusAsync(
                id: id,
                status: "REJECTED",
                externalOrderId: null,
                errorCode: errorCode,
                errorMessage: errorMessage,
                rawResponse: rawResponse,
                sentAt: null,
                updatedAt: updatedAt,
                ct: ct);
        }

        public async Task MarkFailedAsync(Guid id, string? errorCode, string? errorMessage, string? rawResponse, DateTime updatedAt, CancellationToken ct)
        {
            await UpdateStatusAsync(
                id: id,
                status: "FAILED",
                externalOrderId: null,
                errorCode: errorCode,
                errorMessage: errorMessage,
                rawResponse: rawResponse,
                sentAt: null,
                updatedAt: updatedAt,
                ct: ct);
        }

        public async Task<IReadOnlyList<ExecutionRequestRecord>> ListPendingReconciliationAsync(
            int maxCount,
            CancellationToken ct)
        {
            await using var conn = _factory.Create();
            await conn.OpenAsync(ct);

            const string sql = """
                                SELECT
                                    id,
                                    intent_id,
                                    position_id,
                                    action,
                                    token_id,
                                    market_condition_id,
                                    side,
                                    limit_price,
                                    requested_size_usd,
                                    status,
                                    correlation_id,
                                    external_order_id,
                                    error_code,
                                    error_message,
                                    created_at,
                                    sent_at,
                                    updated_at,
                                    raw_request::text AS RawRequest,
                                    raw_response::text AS RawResponse,
                                    materialized_position_id AS MaterializedPositionId,
                                    materialized_at AS MaterializedAt,
                                    last_reconciliation_at AS LastReconciliationAt
                                FROM execution_requests
                                WHERE status IN ('ACCEPTED', 'PARTIALLY_FILLED', 'DISPATCHED')
                                ORDER BY updated_at ASC
                                LIMIT @MaxCount;
                                """;

            var items = await conn.QueryAsync<ExecutionRequestRecord>(
                new CommandDefinition(sql, new { MaxCount = maxCount }, cancellationToken: ct));

            return items.ToList();
        }

        public async Task MarkPartiallyFilledAsync(
            Guid id,
            string? rawResponse,
            DateTime updatedAt,
            CancellationToken ct)
        {
            await UpdateStatusAsync(
                id: id,
                status: "PARTIALLY_FILLED",
                externalOrderId: null,
                errorCode: null,
                errorMessage: null,
                rawResponse: rawResponse,
                sentAt: null,
                updatedAt: updatedAt,
                ct: ct);
        }

        public async Task MarkFilledAsync(
            Guid id,
            string? rawResponse,
            DateTime updatedAt,
            CancellationToken ct)
        {
            await UpdateStatusAsync(
                id: id,
                status: "FILLED",
                externalOrderId: null,
                errorCode: null,
                errorMessage: null,
                rawResponse: rawResponse,
                sentAt: null,
                updatedAt: updatedAt,
                ct: ct);
        }

        public async Task MarkCancelledAsync(
            Guid id,
            string? rawResponse,
            DateTime updatedAt,
            CancellationToken ct)
        {
            await UpdateStatusAsync(
                id: id,
                status: "CANCELLED",
                externalOrderId: null,
                errorCode: null,
                errorMessage: null,
                rawResponse: rawResponse,
                sentAt: null,
                updatedAt: updatedAt,
                ct: ct);
        }

        public async Task MarkExpiredAsync(
            Guid id,
            string? rawResponse,
            DateTime updatedAt,
            CancellationToken ct)
        {
            await UpdateStatusAsync(
                id: id,
                status: "EXPIRED",
                externalOrderId: null,
                errorCode: null,
                errorMessage: null,
                rawResponse: rawResponse,
                sentAt: null,
                updatedAt: updatedAt,
                ct: ct);
        }

        public async Task MarkMaterializedAsync(
        Guid requestId,
        string materializedPositionId,
        DateTime materializedAt,
        DateTime lastReconciliationAt,
        CancellationToken ct)
        {
            await using var conn = _factory.Create();
            await conn.OpenAsync(ct);

            const string sql = """
                                UPDATE execution_requests
                                SET
                                    materialized_position_id = @MaterializedPositionId,
                                    materialized_at = @MaterializedAt,
                                    last_reconciliation_at = @LastReconciliationAt,
                                    updated_at = @LastReconciliationAt
                                WHERE id = @RequestId;
                                """;

            await conn.ExecuteAsync(
                new CommandDefinition(sql, new
                {
                    RequestId = requestId,
                    MaterializedPositionId = materializedPositionId,
                    MaterializedAt = materializedAt,
                    LastReconciliationAt = lastReconciliationAt
                }, cancellationToken: ct));
        }

        public async Task MarkReconciledAsync(
        Guid requestId,
        DateTime lastReconciliationAt,
        CancellationToken ct)
        {
            await using var conn = _factory.Create();
            await conn.OpenAsync(ct);

            const string sql = """
                                UPDATE execution_requests
                                SET
                                    last_reconciliation_at = @LastReconciliationAt,
                                    updated_at = @LastReconciliationAt
                                WHERE id = @RequestId;
                                """;

            await conn.ExecuteAsync(
                new CommandDefinition(sql, new
                {
                    RequestId = requestId,
                    LastReconciliationAt = lastReconciliationAt
                }, cancellationToken: ct));
        }

        private async Task UpdateStatusAsync(
            Guid id,
            string status,
            string? externalOrderId,
            string? errorCode,
            string? errorMessage,
            string? rawResponse,
            DateTime? sentAt,
            DateTime updatedAt,
            CancellationToken ct)
        {
            await using var conn = _factory.Create();
            await conn.OpenAsync(ct);

            const string sql = """
                UPDATE execution_requests
                SET
                    status = @Status,
                    external_order_id = COALESCE(@ExternalOrderId, external_order_id),
                    error_code = @ErrorCode,
                    error_message = @ErrorMessage,
                    sent_at = COALESCE(@SentAt, sent_at),
                    updated_at = @UpdatedAt,
                    raw_response = CASE
                        WHEN @RawResponse IS NULL THEN raw_response
                        ELSE @RawResponse::jsonb
                    END
                WHERE id = @Id;
                """;

            await conn.ExecuteAsync(
                new CommandDefinition(sql, new
                {
                    Id = id,
                    Status = status,
                    ExternalOrderId = externalOrderId,
                    ErrorCode = errorCode,
                    ErrorMessage = errorMessage,
                    SentAt = sentAt,
                    UpdatedAt = updatedAt,
                    RawResponse = rawResponse
                }, cancellationToken: ct));
        }
    }
}