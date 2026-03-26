namespace Arb.Core.Infrastructure.Postgres
{
    public class PostgresOptions
    {
        public const string SectionName = "Postgres";
        public string Connection { get; init; } = "";
    }
}
