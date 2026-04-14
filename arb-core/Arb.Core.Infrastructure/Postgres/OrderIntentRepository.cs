using Arb.Core.Application.Abstractions.Persistence;
using Arb.Core.Contracts.Events;
using Dapper;
using System.Text.Json;

namespace Arb.Core.Infrastructure.Postgres
{
    public class OrderIntentRepository : IOrderIntentRepository
    {
        private readonly NpgsqlConnectionFactory _factory;

        public OrderIntentRepository(NpgsqlConnectionFactory factory)
        {
            _factory = factory;
        }

        public async Task InsertAsync(OrderIntentV1 intent, CancellationToken ct)
        {
            var id = ParseGuid(intent.IntentId);

            await using var conn = _factory.Create();
            await conn.OpenAsync(ct);

            const string sql = """
                INSERT INTO order_intents
                (
                    id,
                    correlation_id,
                    strategy,
                    venue,
                    sport_key,
                    event_key,
                    home_team,
                    away_team,
                    commence_time,
                    market_type,
                    selection_key,
                    price_limit,
                    stake,
                    side,
                    created_at,
                    raw_payload
                )
                VALUES
                (
                    @Id,
                    @CorrelationId,
                    @Strategy,
                    @Venue,
                    @SportKey,
                    @EventKey,
                    @HomeTeam,
                    @AwayTeam,
                    @CommenceTime,
                    @MarketType,
                    @SelectionKey,
                    @PriceLimit,
                    @Stake,
                    @Side,
                    @CreatedAt,
                    @RawPayload::jsonb
                )
                ON CONFLICT (id) DO NOTHING;
                """;

            var payload = JsonSerializer.Serialize(intent);

            await conn.ExecuteAsync(sql, new
            {
                Id = id,
                intent.CorrelationId,
                intent.Strategy,
                intent.Venue,
                intent.SportKey,
                intent.EventKey,
                intent.HomeTeam,
                intent.AwayTeam,
                CommenceTime = intent.CommenceTime,
                intent.MarketType,
                intent.SelectionKey,
                PriceLimit = intent.PriceLimit,
                Stake = intent.Stake,
                intent.Side,
                CreatedAt = intent.Ts,
                RawPayload = payload
            });
        }

        private static Guid ParseGuid(string value)
            => Guid.TryParse(value, out var g) ? g : Guid.ParseExact(value, "N");
    }
}