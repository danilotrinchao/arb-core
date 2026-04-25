namespace Arb.Core.Application.Abstractions.Execution
{
    public class CancelExecutionCommandDto
    {
        public Guid RequestId { get; init; }
        public string ExternalOrderId { get; init; } = string.Empty;
        public string CorrelationId { get; init; } = string.Empty;
        public string Reason { get; init; } = string.Empty;
    }
}