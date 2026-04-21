using Arb.Core.Application.Abstractions.Persistence;
using Dapper;

namespace Arb.Core.Infrastructure.Postgres
{
    public class TokenHealthRepository : ITokenHealthRepository
    {
        private const string StatusNoOrderbook = "NO_ORDERBOOK";
        private const string StatusHealthy = "HEALTHY";

        private readonly NpgsqlConnectionFactory _factory;

        public TokenHealthRepository(NpgsqlConnectionFactory factory)
        {
            _factory = factory;
        }

        public async Task<TokenHealthState?> GetAsync(string tokenId, CancellationToken ct)
        {
            await using var conn = _factory.Create();
            await conn.OpenAsync(ct);

            const string sql = """
                SELECT
                    token_id AS TokenId,
                    status AS Status,
                    reason AS Reason,
                    failure_count AS FailureCount,
                    first_seen_at AS FirstSeenAt,
                    last_seen_at AS LastSeenAt,
                    retry_after AS RetryAfter,
                    last_http_status AS LastHttpStatus,
                    last_response_body AS LastResponseBody
                FROM polymarket_token_health
                WHERE token_id = @TokenId;
                """;

            return await conn.QuerySingleOrDefaultAsync<TokenHealthState>(
                new CommandDefinition(sql, new { TokenId = tokenId }, cancellationToken: ct));
        }

        public async Task<bool> IsBlockedAsync(
            string tokenId,
            DateTime utcNow,
            CancellationToken ct)
        {
            await using var conn = _factory.Create();
            await conn.OpenAsync(ct);

            const string sql = """
                SELECT EXISTS
                (
                    SELECT 1
                    FROM polymarket_token_health
                    WHERE token_id = @TokenId
                      AND status = 'NO_ORDERBOOK'
                      AND retry_after IS NOT NULL
                      AND retry_after > @UtcNow
                );
                """;

            return await conn.ExecuteScalarAsync<bool>(
                new CommandDefinition(sql, new
                {
                    TokenId = tokenId,
                    UtcNow = utcNow
                }, cancellationToken: ct));
        }

        public async Task UpsertNoOrderbookAsync(
            string tokenId,
            int httpStatus,
            string? responseBody,
            DateTime utcNow,
            DateTime retryAfter,
            CancellationToken ct)
        {
            await using var conn = _factory.Create();
            await conn.OpenAsync(ct);

            const string sql = """
                INSERT INTO polymarket_token_health
                (
                    token_id,
                    status,
                    reason,
                    failure_count,
                    first_seen_at,
                    last_seen_at,
                    retry_after,
                    last_http_status,
                    last_response_body
                )
                VALUES
                (
                    @TokenId,
                    @Status,
                    @Reason,
                    1,
                    @UtcNow,
                    @UtcNow,
                    @RetryAfter,
                    @HttpStatus,
                    @ResponseBody
                )
                ON CONFLICT (token_id) DO UPDATE
                SET
                    status = EXCLUDED.status,
                    reason = EXCLUDED.reason,
                    failure_count = polymarket_token_health.failure_count + 1,
                    last_seen_at = EXCLUDED.last_seen_at,
                    retry_after = EXCLUDED.retry_after,
                    last_http_status = EXCLUDED.last_http_status,
                    last_response_body = EXCLUDED.last_response_body;
                """;

            await conn.ExecuteAsync(new CommandDefinition(sql, new
            {
                TokenId = tokenId,
                Status = StatusNoOrderbook,
                Reason = "NO_ORDERBOOK_404",
                UtcNow = utcNow,
                RetryAfter = retryAfter,
                HttpStatus = httpStatus,
                ResponseBody = responseBody
            }, cancellationToken: ct));
        }

        public async Task MarkHealthyAsync(
            string tokenId,
            DateTime utcNow,
            CancellationToken ct)
        {
            await using var conn = _factory.Create();
            await conn.OpenAsync(ct);

            const string sql = """
                INSERT INTO polymarket_token_health
                (
                    token_id,
                    status,
                    reason,
                    failure_count,
                    first_seen_at,
                    last_seen_at,
                    retry_after,
                    last_http_status,
                    last_response_body
                )
                VALUES
                (
                    @TokenId,
                    @Status,
                    @Reason,
                    0,
                    @UtcNow,
                    @UtcNow,
                    NULL,
                    NULL,
                    NULL
                )
                ON CONFLICT (token_id) DO UPDATE
                SET
                    status = EXCLUDED.status,
                    reason = EXCLUDED.reason,
                    last_seen_at = EXCLUDED.last_seen_at,
                    retry_after = NULL,
                    last_http_status = NULL,
                    last_response_body = NULL;
                """;

            await conn.ExecuteAsync(new CommandDefinition(sql, new
            {
                TokenId = tokenId,
                Status = StatusHealthy,
                Reason = "RECOVERED",
                UtcNow = utcNow
            }, cancellationToken: ct));
        }
    }
}