using Arb.Core.Application.Abstractions.Persistence;
using Dapper;

namespace Arb.Core.Infrastructure.Postgres
{
    public sealed class PortfolioRepository : IPortfolioRepository
    {
        private readonly NpgsqlConnectionFactory _factory;

        public PortfolioRepository(NpgsqlConnectionFactory factory)
        {
            _factory = factory;
        }

        public async Task EnsureInitializedAsync(double initialBalance, CancellationToken ct)
        {
            await using var conn = _factory.Create();
            await conn.OpenAsync(ct);

            var exists = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM portfolio_state;");
            if (exists > 0) return;

            const string sql = """
                            INSERT INTO portfolio_state (id, initial_balance, current_balance, updated_at)
                            VALUES (@Id, @InitialBalance, @CurrentBalance, @UpdatedAt);
                            """;

            await conn.ExecuteAsync(sql, new
            {
                Id = Guid.NewGuid(),
                InitialBalance = initialBalance,
                CurrentBalance = initialBalance,
                UpdatedAt = DateTime.Now
            });
        }

        public async Task<PortfolioState?> GetAsync(CancellationToken ct)
        {
            await using var conn = _factory.Create();
            await conn.OpenAsync(ct);

            const string sql = """
                                SELECT
                                    id as Id,
                                    initial_balance as InitialBalance,
                                    current_balance as CurrentBalance,
                                    updated_at as UpdatedAt
                                FROM portfolio_state
                                LIMIT 1;
                                """;

            return await conn.QuerySingleOrDefaultAsync<PortfolioState>(sql);
        }

        public async Task UpdateBalanceAsync(double newBalance, DateTime updatedAt, CancellationToken ct)
        {
            await using var conn = _factory.Create();
            await conn.OpenAsync(ct);

            const string sql = """
                                UPDATE portfolio_state
                                SET current_balance = @CurrentBalance,
                                    updated_at = @UpdatedAt;
                                """;

            await conn.ExecuteAsync(sql, new
            {
                CurrentBalance = newBalance,
                UpdatedAt = updatedAt.Date
            });
        }
    }
}
