using Arb.Core.Application.Abstractions.Persistence;
using Dapper;

namespace Arb.Core.Infrastructure.Postgres
{
    public class PositionRepository : IPositionRepository
    {
        private readonly NpgsqlConnectionFactory _factory;

        public PositionRepository(NpgsqlConnectionFactory factory)
        {
            _factory = factory;
        }

        public async Task<Guid> CreateOpenAsync(PositionOpen position, CancellationToken ct)
        {
            await using var conn = _factory.Create();
            await conn.OpenAsync(ct);

            var id = Guid.NewGuid();

            const string sql = """
                INSERT INTO positions
                    (id, intent_id, sport_key, event_key, home_team, away_team,
                     commence_time, market_type, selection_key, stake, entry_price,
                     target_side, observed_team, polymarket_condition_id,
                     polymarket_entry_price, target_probability, target_token_id,
                     status, created_at)
                VALUES
                    (@Id, @IntentId, @SportKey, @EventKey, @HomeTeam, @AwayTeam,
                     @CommenceTime, @MarketType, @SelectionKey, @Stake, @EntryPrice,
                     @TargetSide, @ObservedTeam, @PolymarketConditionId,
                     @PolymarketEntryPrice, @TargetProbability, @TargetTokenId,
                     'OPEN', @CreatedAt)
                ON CONFLICT (intent_id) DO NOTHING;
                """;

            var affected = await conn.ExecuteAsync(sql, new
            {
                Id = id,
                IntentId = Guid.TryParse(position.IntentId, out var intentGuid)
                    ? intentGuid
                    : Guid.Empty,
                position.SportKey,
                position.EventKey,
                position.HomeTeam,
                position.AwayTeam,
                position.CommenceTime,
                position.MarketType,
                position.SelectionKey,
                position.Stake,
                position.EntryPrice,
                position.TargetSide,
                position.ObservedTeam,
                position.PolymarketConditionId,
                position.PolymarketEntryPrice,
                position.TargetProbability,
                position.TargetTokenId,
                position.CreatedAt
            });

            return affected == 0 ? Guid.Empty : id;
        }

        public async Task CloseAsync(
            Guid positionId,
            double pnl,
            DateTime closedAt,
            double? closePrice,
            string exitReason,
            CancellationToken ct)
        {
            await using var conn = _factory.Create();
            await conn.OpenAsync(ct);

            const string sql = """
                UPDATE positions
                SET status      = 'CLOSED',
                    closed_at   = @ClosedAt,
                    pnl         = @Pnl,
                    close_price = @ClosePrice,
                    exit_reason = @ExitReason
                WHERE id = @Id;
                """;

            await conn.ExecuteAsync(sql, new
            {
                Id = positionId,
                ClosedAt = closedAt,
                Pnl = pnl,
                ClosePrice = closePrice,
                ExitReason = exitReason
            });
        }

        public async Task UpdateLastKnownMidPriceAsync(
            Guid positionId,
            double midPrice,
            DateTime checkedAt,
            CancellationToken ct)
        {
            await using var conn = _factory.Create();
            await conn.OpenAsync(ct);

            const string sql = """
                UPDATE positions
                SET last_known_mid_price  = @MidPrice,
                    last_price_checked_at = @CheckedAt
                WHERE id = @Id
                  AND status = 'OPEN';
                """;

            await conn.ExecuteAsync(sql, new
            {
                Id = positionId,
                MidPrice = midPrice,
                CheckedAt = checkedAt
            });
        }

        public async Task<int> CountOpenAsync(CancellationToken ct)
        {
            await using var conn = _factory.Create();
            await conn.OpenAsync(ct);

            return await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM positions WHERE status = 'OPEN';");
        }

        public async Task<int> CountOpenPolymarketAsync(CancellationToken ct)
        {
            await using var conn = _factory.Create();
            await conn.OpenAsync(ct);

            return await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM positions WHERE status = 'OPEN' AND target_side IS NOT NULL;");
        }

        public async Task<IReadOnlyList<OpenPositionForSettlement>> ListEligibleOpenAsync(
            DateTime nowUtc,
            int maxBatchSize,
            CancellationToken ct)
        {
            await using var conn = _factory.Create();
            await conn.OpenAsync(ct);

            const string sql = """
                SELECT
                    id                      AS Id,
                    intent_id               AS IntentId,
                    sport_key               AS SportKey,
                    event_key               AS EventKey,
                    home_team               AS HomeTeam,
                    away_team               AS AwayTeam,
                    commence_time           AS CommenceTime,
                    market_type             AS MarketType,
                    selection_key           AS SelectionKey,
                    stake                   AS Stake,
                    entry_price             AS EntryPrice,
                    created_at              AS CreatedAt,
                    target_side             AS TargetSide,
                    observed_team           AS ObservedTeam,
                    polymarket_condition_id AS PolymarketConditionId,
                    polymarket_entry_price  AS PolymarketEntryPrice,
                    target_probability      AS TargetProbability,
                    last_known_mid_price    AS LastKnownMidPrice,
                    last_price_checked_at   AS LastPriceCheckedAt,
                    target_token_id         AS TargetTokenId
                FROM positions
                WHERE status = 'OPEN'
                  AND commence_time <= @NowUtc
                  AND target_side IS NULL
                ORDER BY commence_time ASC
                LIMIT @MaxBatchSize;
                """;

            var result = await conn.QueryAsync<OpenPositionForSettlement>(sql, new
            {
                NowUtc = nowUtc,
                MaxBatchSize = maxBatchSize
            });

            return result.ToList();
        }

        public async Task<IReadOnlyList<OpenPositionForSettlement>> ListOpenPolymarketPositionsAsync(
            CancellationToken ct)
        {
            await using var conn = _factory.Create();
            await conn.OpenAsync(ct);

            const string sql = """
                SELECT
                    id                      AS Id,
                    intent_id               AS IntentId,
                    sport_key               AS SportKey,
                    event_key               AS EventKey,
                    home_team               AS HomeTeam,
                    away_team               AS AwayTeam,
                    commence_time           AS CommenceTime,
                    market_type             AS MarketType,
                    selection_key           AS SelectionKey,
                    stake                   AS Stake,
                    entry_price             AS EntryPrice,
                    created_at              AS CreatedAt,
                    target_side             AS TargetSide,
                    observed_team           AS ObservedTeam,
                    polymarket_condition_id AS PolymarketConditionId,
                    polymarket_entry_price  AS PolymarketEntryPrice,
                    target_probability      AS TargetProbability,
                    last_known_mid_price    AS LastKnownMidPrice,
                    last_price_checked_at   AS LastPriceCheckedAt,
                    target_token_id         AS TargetTokenId
                FROM positions
                WHERE status = 'OPEN'
                  AND target_side IS NOT NULL
                ORDER BY commence_time ASC;
                """;

            var result = await conn.QueryAsync<OpenPositionForSettlement>(sql);

            return result.ToList();
        }

        public async Task<IReadOnlyList<OpenPositionForSettlement>> ListOpenPolymarketPositionsByDateAsync(
            DateOnly date,
            CancellationToken ct)
        {
            await using var conn = _factory.Create();
            await conn.OpenAsync(ct);

            const string sql = """
                SELECT
                    id                      AS Id,
                    intent_id               AS IntentId,
                    sport_key               AS SportKey,
                    event_key               AS EventKey,
                    home_team               AS HomeTeam,
                    away_team               AS AwayTeam,
                    commence_time           AS CommenceTime,
                    market_type             AS MarketType,
                    selection_key           AS SelectionKey,
                    stake                   AS Stake,
                    entry_price             AS EntryPrice,
                    created_at              AS CreatedAt,
                    target_side             AS TargetSide,
                    observed_team           AS ObservedTeam,
                    polymarket_condition_id AS PolymarketConditionId,
                    polymarket_entry_price  AS PolymarketEntryPrice,
                    target_probability      AS TargetProbability,
                    last_known_mid_price    AS LastKnownMidPrice,
                    last_price_checked_at   AS LastPriceCheckedAt,
                    target_token_id         AS TargetTokenId
                FROM positions
                WHERE status = 'OPEN'
                  AND target_side IS NOT NULL
                  AND commence_time::date = @Date
                ORDER BY created_at ASC;
                """;

            var result = await conn.QueryAsync<OpenPositionForSettlement>(sql, new
            {
                Date = date.ToString("yyyy-MM-dd")
            });

            return result.ToList();
        }
    }
}