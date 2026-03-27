using Arb.Core.Application.Abstractions.Persistence;
using Arb.Core.Contracts.Events;
using Dapper;
using System.Text.Json;

namespace Arb.Core.Infrastructure.Postgres
{
    public sealed class ExecutionReportRepository : IExecutionReportRepository
    {
        private readonly NpgsqlConnectionFactory _factory;

        public ExecutionReportRepository(NpgsqlConnectionFactory factory)
        {
            _factory = factory;
        }

        public async Task InsertAsync(ExecutionReportV1 report, CancellationToken ct)
        {
            var reportId = ParseGuid(report.ReportId);
            var intentId = ParseGuid(report.IntentId);

            await using var conn = _factory.Create();
            await conn.OpenAsync(ct);

            const string sql = """
                                INSERT INTO execution_reports
                                (id, intent_id, correlation_id, status, filled_price, filled_usd, tx_hash, error, created_at, raw_payload)
                                VALUES
                                (@Id, @IntentId, @CorrelationId, @Status, @FilledPrice, @FilledUsd, @TxHash, @Error, @CreatedAt, @RawPayload::jsonb);
                                """;

            var payload = JsonSerializer.Serialize(report);

            await conn.ExecuteAsync(sql, new
            {
                Id = reportId,
                IntentId = intentId,
                report.CorrelationId,
                report.Status,
                report.FilledPrice,
                report.FilledUsd,
                report.TxHash,
                report.Error,
                CreatedAt = report.Ts.Date,
                RawPayload = payload
            });
        }

        private static Guid ParseGuid(string value)
            => Guid.TryParse(value, out var g) ? g : Guid.ParseExact(value, "N");
    }
}
