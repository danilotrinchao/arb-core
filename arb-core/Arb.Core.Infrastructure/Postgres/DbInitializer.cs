using Dapper;

namespace Arb.Core.Infrastructure.Postgres
{
    public class DbInitializer
    {
        private readonly NpgsqlConnectionFactory _factory;

        public DbInitializer(NpgsqlConnectionFactory factory)
        {
            _factory = factory;
        }

        public async Task InitializeAsync(CancellationToken ct)
        {
            await using var conn = _factory.Create();
            await conn.OpenAsync(ct);

            const string sql = """
            CREATE TABLE IF NOT EXISTS order_intents (
                id              UUID PRIMARY KEY,
                correlation_id  TEXT NOT NULL,
                strategy        VARCHAR(100) NOT NULL,
                venue           VARCHAR(50) NOT NULL,
                sport_key       VARCHAR(100) NOT NULL,
                event_key       VARCHAR(200) NOT NULL,
                home_team       VARCHAR(200) NOT NULL,
                away_team       VARCHAR(200) NOT NULL,
                commence_time   TIMESTAMPTZ NOT NULL,
                market_type     VARCHAR(50) NOT NULL,
                selection_key   VARCHAR(50) NOT NULL,
                price_limit     DOUBLE PRECISION NOT NULL,
                stake           DOUBLE PRECISION NOT NULL,
                side            VARCHAR(20) NOT NULL,
                created_at      TIMESTAMPTZ NOT NULL,
                raw_payload     JSONB NOT NULL
            );

            CREATE TABLE IF NOT EXISTS order_intent_rejections (
                id                      UUID PRIMARY KEY,
                intent_id               TEXT NOT NULL,
                sport_key               VARCHAR(100) NOT NULL,
                observed_team           VARCHAR(200) NOT NULL,
                target_side             VARCHAR(20) NULL,
                polymarket_condition_id VARCHAR(200) NULL,
                target_token_id         VARCHAR(200) NULL,
                reason                  VARCHAR(100) NOT NULL,
                entry_mid               DOUBLE PRECISION NULL,
                comparable_target       DOUBLE PRECISION NULL,
                raw_target_probability  DOUBLE PRECISION NULL,
                headroom_to_target      DOUBLE PRECISION NULL,
                time_to_kickoff_seconds DOUBLE PRECISION NULL,
                intent_generated_at     TIMESTAMPTZ NULL,
                intent_age_seconds      DOUBLE PRECISION NULL,
                created_at              TIMESTAMPTZ NOT NULL,
                raw_payload             JSONB NOT NULL
            );

            CREATE TABLE IF NOT EXISTS execution_reports (
                id              UUID PRIMARY KEY,
                intent_id       UUID NOT NULL,
                correlation_id  TEXT NOT NULL,
                status          VARCHAR(30) NOT NULL,
                filled_price    DOUBLE PRECISION NULL,
                filled_usd      DOUBLE PRECISION NULL,
                tx_hash         VARCHAR(200) NULL,
                error           TEXT NULL,
                created_at      TIMESTAMPTZ NOT NULL,
                raw_payload     JSONB NOT NULL
            );

            CREATE TABLE IF NOT EXISTS portfolio_state (
                id              UUID PRIMARY KEY,
                initial_balance DOUBLE PRECISION NOT NULL,
                current_balance DOUBLE PRECISION NOT NULL,
                updated_at      TIMESTAMPTZ NOT NULL
            );

            CREATE TABLE IF NOT EXISTS positions (
                id                          UUID PRIMARY KEY,
                intent_id                   UUID NOT NULL,
                sport_key                   VARCHAR(100) NOT NULL,
                event_key                   VARCHAR(200) NOT NULL,
                home_team                   VARCHAR(200) NOT NULL,
                away_team                   VARCHAR(200) NOT NULL,
                commence_time               TIMESTAMPTZ NOT NULL,
                market_type                 VARCHAR(50) NOT NULL,
                selection_key               VARCHAR(50) NOT NULL,
                target_side                 VARCHAR(10) NULL,
                stake                       DOUBLE PRECISION NOT NULL,
                entry_price                 DOUBLE PRECISION NOT NULL,
                status                      VARCHAR(20) NOT NULL,
                pnl                         DOUBLE PRECISION NULL,
                closed_at                   TIMESTAMPTZ NULL,
                created_at                  TIMESTAMPTZ NOT NULL,
                observed_team               VARCHAR(200) NULL,
                polymarket_condition_id     VARCHAR(200) NULL,
                polymarket_entry_price      DOUBLE PRECISION NULL,
                target_probability          DOUBLE PRECISION NULL,
                target_token_id             VARCHAR(200) NULL,
                last_known_mid_price        DOUBLE PRECISION NULL,
                last_price_checked_at       TIMESTAMPTZ NULL,
                close_price                 DOUBLE PRECISION NULL,
                exit_reason                 VARCHAR(50) NULL
            );

            CREATE TABLE IF NOT EXISTS position_closure_analytics (
                position_id                             UUID PRIMARY KEY,
                intent_id                               UUID NOT NULL,
                sport_key                               VARCHAR(100) NOT NULL,
                event_key                               VARCHAR(200) NOT NULL,
                market_type                             VARCHAR(50) NOT NULL,
                selection_key                           VARCHAR(50) NOT NULL,
                target_side                             VARCHAR(10) NULL,
                observed_team                           VARCHAR(200) NULL,
                polymarket_condition_id                 VARCHAR(200) NULL,
                target_token_id                         VARCHAR(200) NULL,
                commence_time                           TIMESTAMPTZ NOT NULL,
                opened_at                               TIMESTAMPTZ NOT NULL,
                closed_at                               TIMESTAMPTZ NOT NULL,
                stake                                   DOUBLE PRECISION NOT NULL,
                entry_price                             DOUBLE PRECISION NOT NULL,
                polymarket_entry_price                  DOUBLE PRECISION NULL,
                close_price                             DOUBLE PRECISION NULL,
                pnl                                     DOUBLE PRECISION NULL,
                exit_reason                             VARCHAR(50) NOT NULL,
                target_probability                      DOUBLE PRECISION NULL,
                last_known_mid_price                    DOUBLE PRECISION NULL,
                last_price_checked_at                   TIMESTAMPTZ NULL,
                time_to_kickoff_at_entry_seconds        DOUBLE PRECISION NOT NULL,
                time_to_kickoff_at_close_seconds        DOUBLE PRECISION NOT NULL,
                had_missing_midpoint_at_close           BOOLEAN NOT NULL,
                used_last_known_mid_price_fallback      BOOLEAN NOT NULL,
                created_at                              TIMESTAMPTZ NOT NULL
            );

            CREATE TABLE IF NOT EXISTS polymarket_token_health (
                token_id            TEXT PRIMARY KEY,
                status              TEXT NOT NULL,
                reason              TEXT NOT NULL,
                failure_count       INT NOT NULL DEFAULT 0,
                first_seen_at       TIMESTAMPTZ NOT NULL,
                last_seen_at        TIMESTAMPTZ NOT NULL,
                retry_after         TIMESTAMPTZ NULL,
                last_http_status    INT NULL,
                last_response_body  TEXT NULL
            );

            ALTER TABLE positions ADD COLUMN IF NOT EXISTS observed_team VARCHAR(200) NULL;
            ALTER TABLE positions ADD COLUMN IF NOT EXISTS polymarket_condition_id VARCHAR(200) NULL;
            ALTER TABLE positions ADD COLUMN IF NOT EXISTS polymarket_entry_price DOUBLE PRECISION NULL;
            ALTER TABLE positions ADD COLUMN IF NOT EXISTS target_probability DOUBLE PRECISION NULL;
            ALTER TABLE positions ADD COLUMN IF NOT EXISTS target_token_id VARCHAR(200) NULL;
            ALTER TABLE positions ADD COLUMN IF NOT EXISTS last_known_mid_price DOUBLE PRECISION NULL;
            ALTER TABLE positions ADD COLUMN IF NOT EXISTS last_price_checked_at TIMESTAMPTZ NULL;
            ALTER TABLE positions ADD COLUMN IF NOT EXISTS close_price DOUBLE PRECISION NULL;
            ALTER TABLE positions ADD COLUMN IF NOT EXISTS exit_reason VARCHAR(50) NULL;

            ALTER TABLE order_intent_rejections
                ADD COLUMN IF NOT EXISTS raw_target_probability DOUBLE PRECISION NULL;

            ALTER TABLE order_intent_rejections
                ADD COLUMN IF NOT EXISTS intent_generated_at TIMESTAMPTZ NULL;

            ALTER TABLE order_intent_rejections
                ADD COLUMN IF NOT EXISTS intent_age_seconds DOUBLE PRECISION NULL;

            CREATE UNIQUE INDEX IF NOT EXISTS uq_positions_intent_id
                ON positions(intent_id);

            CREATE UNIQUE INDEX IF NOT EXISTS uq_positions_open_market_slot
                ON positions (
                    sport_key,
                    event_key,
                    polymarket_condition_id,
                    target_token_id,
                    target_side
                )
                WHERE status = 'OPEN'
                  AND target_side IS NOT NULL
                  AND polymarket_condition_id IS NOT NULL
                  AND target_token_id IS NOT NULL;

            CREATE INDEX IF NOT EXISTS ix_positions_status
                ON positions(status);

            CREATE INDEX IF NOT EXISTS ix_positions_commence_time
                ON positions(commence_time);

            CREATE INDEX IF NOT EXISTS ix_positions_open_polymarket
                ON positions(status, target_side, commence_time)
                WHERE target_side IS NOT NULL;

            CREATE INDEX IF NOT EXISTS ix_order_intents_created_at
                ON order_intents(created_at);

            CREATE INDEX IF NOT EXISTS ix_order_intents_sport_key
                ON order_intents(sport_key);

            CREATE INDEX IF NOT EXISTS ix_order_intent_rejections_created_at
                ON order_intent_rejections(created_at);

            CREATE INDEX IF NOT EXISTS ix_order_intent_rejections_reason
                ON order_intent_rejections(reason);

            CREATE INDEX IF NOT EXISTS ix_order_intent_rejections_sport_key
                ON order_intent_rejections(sport_key);

            CREATE INDEX IF NOT EXISTS ix_execution_reports_intent_id
                ON execution_reports(intent_id);

            CREATE INDEX IF NOT EXISTS ix_position_closure_analytics_closed_at
                ON position_closure_analytics(closed_at);

            CREATE INDEX IF NOT EXISTS ix_position_closure_analytics_sport_key
                ON position_closure_analytics(sport_key);

            CREATE INDEX IF NOT EXISTS ix_position_closure_analytics_exit_reason
                ON position_closure_analytics(exit_reason);

            CREATE INDEX IF NOT EXISTS ix_position_closure_analytics_intent_id
                ON position_closure_analytics(intent_id);

            CREATE INDEX IF NOT EXISTS ix_polymarket_token_health_status_retry_after
                ON polymarket_token_health(status, retry_after);

            CREATE INDEX IF NOT EXISTS ix_polymarket_token_health_last_seen_at
                ON polymarket_token_health(last_seen_at);
            """;

            await conn.ExecuteAsync(sql);
        }
    }
}