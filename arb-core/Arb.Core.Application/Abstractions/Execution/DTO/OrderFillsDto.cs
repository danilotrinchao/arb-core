namespace Arb.Core.Application.Abstractions.Execution
{
    public class OrderFillsDto
    {
        public bool Success { get; init; }
        public string ExternalOrderId { get; init; } = string.Empty;
        public IReadOnlyList<OrderFillItemDto> Fills { get; init; } = Array.Empty<OrderFillItemDto>();
        public string? RawJson { get; init; }
    }

    public class OrderFillItemDto
    {
        public string ExternalFillId { get; init; } = string.Empty;
        public double Price { get; init; }
        public double SizeUsd { get; init; }
        public DateTime ExecutedAt { get; init; }
        public string Side { get; init; } = string.Empty;
    }
}