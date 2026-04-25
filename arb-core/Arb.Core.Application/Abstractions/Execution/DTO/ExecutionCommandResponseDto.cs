namespace Arb.Core.Application.Abstractions.Execution
{
    public class ExecutionCommandResponseDto
    {
        public bool Success { get; init; }
        public Guid RequestId { get; init; }
        public string? ExternalOrderId { get; init; }
        public string Status { get; init; } = string.Empty;
        public string? ErrorCode { get; init; }
        public string? ErrorMessage { get; init; }
        public string? RawJson { get; init; }
    }
}