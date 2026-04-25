namespace Arb.Core.Application.Abstractions.Execution
{
    public class ExecutionCommandDto
    {
        public Guid RequestId { get; init; }
        public Guid? IntentId { get; init; }
        public Guid? PositionId { get; init; }
        public string TokenId { get; init; } = string.Empty;
        public string? MarketConditionId { get; init; }
        public string Side { get; init; } = string.Empty;
        public double Price { get; init; }
        public double SizeUsd { get; init; }
        public string TimeInForce { get; init; } = "GTC";
        public string CorrelationId { get; init; } = string.Empty;
        public Dictionary<string, object>? Metadata { get; init; }
    }
}