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

                    -- Campos do fluxo Polymarket
                    observed_team               VARCHAR(200) NULL,
                    polymarket_condition_id     VARCHAR(200) NULL,

                    -- Preço real de entrada na Polymarket (midpoint CLOB no momento da abertura)
                    -- Separado de entry_price que continha a odd asiática (escala errada)
                    polymarket_entry_price      DOUBLE PRECISION NULL,

                    -- Alvo de convergência em probabilidade implícita (1 / odd_decimal asiática)
                    -- O monitor fecha quando mid_price >= target_probability
                    target_probability          DOUBLE PRECISION NULL,

                    -- Último midpoint consultado com sucesso na CLOB
                    -- Usado como fallback quando a CLOB falha no momento do kickoff
                    last_known_mid_price        DOUBLE PRECISION NULL,

                    -- Timestamp da última consulta bem-sucedida
                    -- Permite detectar preços stale (muito antigos para ser confiáveis)
                    last_price_checked_at       TIMESTAMPTZ NULL,

                    -- Preço real de saída no momento do fechamento
                    close_price                 DOUBLE PRECISION NULL,

                    -- Motivo do fechamento:
                    -- CONVERGED           → mid_price atingiu target_probability
                    -- KICKOFF_FALLBACK    → fechado antes do kickoff por tempo
                    -- KICKOFF_NO_PRICE    → fechado antes do kickoff sem preço confiável
                    -- EXPIRED_NO_CLOSE    → jogo começou com posição ainda aberta
                    -- MANUAL             → fechado manualmente
                    exit_reason                 VARCHAR(50) NULL
                );

                -- Adições incrementais para ambientes existentes
                -- ADD COLUMN IF NOT EXISTS não falha se a coluna já existir
                ALTER TABLE positions ADD COLUMN IF NOT EXISTS observed_team VARCHAR(200) NULL;
                ALTER TABLE positions ADD COLUMN IF NOT EXISTS polymarket_condition_id VARCHAR(200) NULL;
                ALTER TABLE positions ADD COLUMN IF NOT EXISTS polymarket_entry_price DOUBLE PRECISION NULL;
                ALTER TABLE positions ADD COLUMN IF NOT EXISTS target_probability DOUBLE PRECISION NULL;
                ALTER TABLE positions ADD COLUMN IF NOT EXISTS last_known_mid_price DOUBLE PRECISION NULL;
                ALTER TABLE positions ADD COLUMN IF NOT EXISTS last_price_checked_at TIMESTAMPTZ NULL;
                ALTER TABLE positions ADD COLUMN IF NOT EXISTS close_price DOUBLE PRECISION NULL;
                ALTER TABLE positions ADD COLUMN IF NOT EXISTS exit_reason VARCHAR(50) NULL;

                -- Índices
                CREATE UNIQUE INDEX IF NOT EXISTS uq_positions_intent_id
                    ON positions(intent_id);

                CREATE INDEX IF NOT EXISTS ix_positions_status
                    ON positions(status);

                CREATE INDEX IF NOT EXISTS ix_positions_commence_time
                    ON positions(commence_time);

                -- Índice composto para a query principal do monitor de saída:
                -- busca posições abertas do fluxo Polymarket ordenadas por kickoff
                CREATE INDEX IF NOT EXISTS ix_positions_open_polymarket
                    ON positions(status, target_side, commence_time)
                    WHERE target_side IS NOT NULL;

                CREATE INDEX IF NOT EXISTS ix_order_intents_created_at
                    ON order_intents(created_at);

                CREATE INDEX IF NOT EXISTS ix_execution_reports_intent_id
                    ON execution_reports(intent_id);
                """;

            await conn.ExecuteAsync(sql);
        }
    }
}