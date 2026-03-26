namespace Arb.Core.Contracts.Events
{
    public sealed record ExecutionReportV1(
      string SchemaVersion,
      string ReportId,
      string IntentId,
      string CorrelationId,
      DateTime Ts,
      string Status,           // FILLED | REJECTED | FAILED
      double? FilledPrice,
      double? FilledUsd,
      string? TxHash,
      string? Error
  );

}
