namespace Arb.Core.Executor.Worker.Options
{
    public sealed class ExecutorOptions
    {
        public const string SectionName = "Executor";

        public double InitialBalance { get; init; } = 1000.0;
        public double WinRateAssumption { get; init; } = 0.55;
        public double SettlementSeconds { get; init; } = 30.0;

        // "Paper" ou "Real"
        public string ExecutionMode { get; init; } = "Paper";
    }
}
