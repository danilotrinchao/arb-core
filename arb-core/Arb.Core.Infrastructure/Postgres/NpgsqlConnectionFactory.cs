using Microsoft.Extensions.Options;
using Npgsql;

namespace Arb.Core.Infrastructure.Postgres
{
    public sealed class NpgsqlConnectionFactory
    {
        private readonly string _connectionString;

        public NpgsqlConnectionFactory(IOptions<PostgresOptions> options)
        {
            _connectionString = options.Value.Connection;
        }

        public NpgsqlConnection Create()
            => new NpgsqlConnection(_connectionString);
    }
}
