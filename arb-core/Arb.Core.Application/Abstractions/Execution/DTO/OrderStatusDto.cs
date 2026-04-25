namespace Arb.Core.Application.Abstractions.Execution
{
    public class OrderStatusDto
    {
        public bool Success { get; init; }
        public string ExternalOrderId { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public double? FilledSizeUsd { get; init; }
        public double? RemainingSizeUsd { get; init; }
        public double? AverageFillPrice { get; init; }
        public string? RawJson { get; init; }
    }
}