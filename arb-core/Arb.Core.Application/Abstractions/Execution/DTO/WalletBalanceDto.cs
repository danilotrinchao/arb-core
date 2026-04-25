namespace Arb.Core.Application.Abstractions.Execution
{
    public class WalletBalanceDto
    {
        public bool Success { get; init; }
        public string WalletAddress { get; init; } = string.Empty;
        public double AvailableUsd { get; init; }
        public double TotalUsd { get; init; }
        public string? RawJson { get; init; }
    }
}