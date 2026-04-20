using Arb.Core.Application.Abstractions.Persistence;
using Dapper;

namespace Arb.Core.Infrastructure.Postgres
{
    public class PositionAnalyticsRepository : IPositionAnalyticsRepository
    {
        private readonly NpgsqlConnectionFactory _factory;

        public PositionAnalyticsRepository(NpgsqlConnectionFactory factory)
        {
            _factory = factory;
        }

        public async Task InsertClosureAnalyticsAsync(
            PositionClosureAnalytics analytics,
            CancellationToken ct)
        {
            await using var conn = _factory.Create();
            await conn.OpenAsync(ct);

            const string sql = """
                INSERT INTO position_closure_analytics
                (
                    position_id,
                    intent_id,
                    sport_key,
                    event_key,
                    market_type,
                    selection_key,
                    target_side,
                    observed_team,
                    polymarket_condition_id,
                    target_token_id,
                    commence_time,
                    opened_at,
                    closed_at,
                    stake,
                    entry_price,
                    polymarket_entry_price,
                    close_price,
                    pnl,
                    exit_reason,
                    target_probability,
                    last_known_mid_price,
                    last_price_checked_at,
                    time_to_kickoff_at_entry_seconds,
                    time_to_kickoff_at_close_seconds,
                    had_missing_midpoint_at_close,
                    used_last_known_mid_price_fallback,
                    created_at
                )
                VALUES
                (
                    @PositionId,
                    @IntentId,
                    @SportKey,
                    @EventKey,
                    @MarketType,
                    @SelectionKey,
                    @TargetSide,
                    @ObservedTeam,
                    @PolymarketConditionId,
                    @TargetTokenId,
                    @CommenceTime,
                    @OpenedAt,
                    @ClosedAt,
                    @Stake,
                    @EntryPrice,
                    @PolymarketEntryPrice,
                    @ClosePrice,
                    @PnL,
                    @ExitReason,
                    @TargetProbability,
                    @LastKnownMidPrice,
                    @LastPriceCheckedAt,
                    @TimeToKickoffAtEntrySeconds,
                    @TimeToKickoffAtCloseSeconds,
                    @HadMissingMidpointAtClose,
                    @UsedLastKnownMidPriceFallback,
                    @CreatedAt
                )
                ON CONFLICT (position_id) DO UPDATE
                SET
                    intent_id = EXCLUDED.intent_id,
                    sport_key = EXCLUDED.sport_key,
                    event_key = EXCLUDED.event_key,
                    market_type = EXCLUDED.market_type,
                    selection_key = EXCLUDED.selection_key,
                    target_side = EXCLUDED.target_side,
                    observed_team = EXCLUDED.observed_team,
                    polymarket_condition_id = EXCLUDED.polymarket_condition_id,
                    target_token_id = EXCLUDED.target_token_id,
                    commence_time = EXCLUDED.commence_time,
                    opened_at = EXCLUDED.opened_at,
                    closed_at = EXCLUDED.closed_at,
                    stake = EXCLUDED.stake,
                    entry_price = EXCLUDED.entry_price,
                    polymarket_entry_price = EXCLUDED.polymarket_entry_price,
                    close_price = EXCLUDED.close_price,
                    pnl = EXCLUDED.pnl,
                    exit_reason = EXCLUDED.exit_reason,
                    target_probability = EXCLUDED.target_probability,
                    last_known_mid_price = EXCLUDED.last_known_mid_price,
                    last_price_checked_at = EXCLUDED.last_price_checked_at,
                    time_to_kickoff_at_entry_seconds = EXCLUDED.time_to_kickoff_at_entry_seconds,
                    time_to_kickoff_at_close_seconds = EXCLUDED.time_to_kickoff_at_close_seconds,
                    had_missing_midpoint_at_close = EXCLUDED.had_missing_midpoint_at_close,
                    used_last_known_mid_price_fallback = EXCLUDED.used_last_known_mid_price_fallback,
                    created_at = EXCLUDED.created_at;
                """;

            await conn.ExecuteAsync(sql, new
            {
                analytics.PositionId,
                analytics.IntentId,
                analytics.SportKey,
                analytics.EventKey,
                analytics.MarketType,
                analytics.SelectionKey,
                analytics.TargetSide,
                analytics.ObservedTeam,
                analytics.PolymarketConditionId,
                analytics.TargetTokenId,
                analytics.CommenceTime,
                analytics.OpenedAt,
                analytics.ClosedAt,
                analytics.Stake,
                analytics.EntryPrice,
                analytics.PolymarketEntryPrice,
                analytics.ClosePrice,
                analytics.PnL,
                analytics.ExitReason,
                analytics.TargetProbability,
                analytics.LastKnownMidPrice,
                analytics.LastPriceCheckedAt,
                analytics.TimeToKickoffAtEntrySeconds,
                analytics.TimeToKickoffAtCloseSeconds,
                analytics.HadMissingMidpointAtClose,
                analytics.UsedLastKnownMidPriceFallback,
                CreatedAt = DateTime.UtcNow
            });
        }
    }
}