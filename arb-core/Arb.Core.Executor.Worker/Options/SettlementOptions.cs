namespace Arb.Core.Executor.Worker.Options
{
    public class SettlementOptions
    {
        public const string SectionName = "Settlement";

        public int PollIntervalSeconds { get; init; } = 60;
        public int MaxBatchSize { get; init; } = 100;

        public int MinutesBeforeKickoffToClose { get; init; } = 30;
        public int MinutesBeforeKickoffToEarlyExit { get; init; } = 180;

        // Gap mínimo entre comparable target e preço atual
        public double MinGapToTargetForEarlyExit { get; init; } = 0.05d;

        // Queda mínima em relação à entrada para considerar deterioração real
        public double MinAdverseMoveToEarlyExit { get; init; } = 0.01d;

        // Faixa considerada "flat" em relação à entrada
        public double FlatMoveToleranceForEarlyExit { get; init; } = 0.005d;

        // Se estiver positivo acima disso, protegemos a posição do early exit
        public double ProtectProfitableMoveFromEarlyExit { get; init; } = 0.015d;

        // Só permitimos early exit de posições flat/slight green quando já estiver bem perto do kickoff
        public int LateWindowMinutesForFlatEarlyExit { get; init; } = 60;

        public int ExitMonitorIntervalSeconds { get; init; } = 30;
        public int MaxPriceAgeMinutes { get; init; } = 10;
    }
}