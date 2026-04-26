using Arb.Core.Application.Abstractions.Persistence;
using Dapper;

namespace Arb.Core.Infrastructure.Postgres
{
    public class OrderIntentRejectionRepository : IOrderIntentRejectionRepository
    {
        private readonly NpgsqlConnectionFactory _factory;

        public OrderIntentRejectionRepository(NpgsqlConnectionFactory factory)
        {
            _factory = factory;
        }

        public async Task InsertAsync(OrderIntentRejection rejection, CancellationToken ct)
        {
            await using var conn = _factory.Create();
            await conn.OpenAsync(ct);

            const string sql = """
                INSERT INTO order_intent_rejections
                (
                    id,
                    intent_id,
                    sport_key,
                    observed_team,
                    target_side,
                    polymarket_condition_id,
                    target_token_id,
                    reason,
                    entry_mid,
                    comparable_target,
                    raw_target_probability,
                    headroom_to_target,
                    initial_edge,
                    delta_vs_comparable_target,
                    time_to_kickoff_seconds,
                    intent_generated_at,
                    intent_age_seconds,
                    created_at,
                    raw_payload
                )
                VALUES
                (
                    @Id,
                    @IntentId,
                    @SportKey,
                    @ObservedTeam,
                    @TargetSide,
                    @PolymarketConditionId,
                    @TargetTokenId,
                    @Reason,
                    @EntryMid,
                    @ComparableTarget,
                    @RawTargetProbability,
                    @HeadroomToTarget,
                    @InitialEdge,
                    @DeltaVsComparableTarget,
                    @TimeToKickoffSeconds,
                    @IntentGeneratedAt,
                    @IntentAgeSeconds,
                    @CreatedAt,
                    @RawPayload::jsonb
                );
                """;

            await conn.ExecuteAsync(sql, new
            {
                rejection.Id,
                rejection.IntentId,
                rejection.SportKey,
                rejection.ObservedTeam,
                rejection.TargetSide,
                rejection.PolymarketConditionId,
                rejection.TargetTokenId,
                rejection.Reason,
                rejection.EntryMid,
                rejection.ComparableTarget,
                rejection.RawTargetProbability,
                rejection.HeadroomToTarget,
                rejection.InitialEdge,
                rejection.DeltaVsComparableTarget,
                rejection.TimeToKickoffSeconds,
                rejection.IntentGeneratedAt,
                rejection.IntentAgeSeconds,
                rejection.CreatedAt,
                rejection.RawPayload
            });
        }
    }
}