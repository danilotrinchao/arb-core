using Arb.Core.Application.Abstractions.Persistence;
using Dapper;

namespace Arb.Core.Infrastructure.Postgres
{
    public class ExecutionFillRepository : IExecutionFillRepository
    {
        private readonly NpgsqlConnectionFactory _factory;

        public ExecutionFillRepository(NpgsqlConnectionFactory factory)
        {
            _factory = factory;
        }

        public async Task InsertAsync(ExecutionFillRecord fill, CancellationToken ct)
        {
            await using var conn = _factory.Create();
            await conn.OpenAsync(ct);

            const string sql = """
                INSERT INTO execution_fills
                (
                    id,
                    execution_request_id,
                    external_order_id,
                    external_fill_id,
                    token_id,
                    side,
                    fill_price,
                    fill_size_usd,
                    executed_at,
                    raw_payload
                )
                VALUES
                (
                    @Id,
                    @ExecutionRequestId,
                    @ExternalOrderId,
                    @ExternalFillId,
                    @TokenId,
                    @Side,
                    @FillPrice,
                    @FillSizeUsd,
                    @ExecutedAt,
                    @RawPayload::jsonb
                )
                ON CONFLICT DO NOTHING;
                """;

            await conn.ExecuteAsync(
                new CommandDefinition(sql, new
                {
                    fill.Id,
                    fill.ExecutionRequestId,
                    fill.ExternalOrderId,
                    fill.ExternalFillId,
                    fill.TokenId,
                    fill.Side,
                    fill.FillPrice,
                    fill.FillSizeUsd,
                    fill.ExecutedAt,
                    fill.RawPayload
                }, cancellationToken: ct));
        }

        public async Task<bool> ExistsByExternalFillIdAsync(string externalFillId, CancellationToken ct)
        {
            await using var conn = _factory.Create();
            await conn.OpenAsync(ct);

            const string sql = """
                SELECT EXISTS
                (
                    SELECT 1
                    FROM execution_fills
                    WHERE external_fill_id = @ExternalFillId
                );
                """;

            return await conn.ExecuteScalarAsync<bool>(
                new CommandDefinition(sql, new
                {
                    ExternalFillId = externalFillId
                }, cancellationToken: ct));
        }

        public async Task<double> SumFilledSizeUsdAsync(Guid executionRequestId, CancellationToken ct)
        {
            await using var conn = _factory.Create();
            await conn.OpenAsync(ct);

            const string sql = """
                SELECT COALESCE(SUM(fill_size_usd), 0)
                FROM execution_fills
                WHERE execution_request_id = @ExecutionRequestId;
                """;

            return await conn.ExecuteScalarAsync<double>(
                new CommandDefinition(sql, new
                {
                    ExecutionRequestId = executionRequestId
                }, cancellationToken: ct));
        }
    }
}