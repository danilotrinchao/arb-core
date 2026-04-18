namespace Arb.Core.Executor.Worker.Options
{
    public class SettlementOptions
    {
        public const string SectionName = "Settlement";

        public int PollIntervalSeconds { get; init; } = 60;
        public int MaxBatchSize { get; init; } = 100;

        // Janela final de fechamento obrigatório antes do kickoff
        public int MinutesBeforeKickoffToClose { get; init; } = 30;

        public int ExitMonitorIntervalSeconds { get; init; } = 30;

        // Idade máxima do preço salvo para fallback
        public int MaxPriceAgeMinutes { get; init; } = 10;

        // Novo: janela anterior ao kickoff para encerrar posição
        // que segue longe do alvo de convergência.
        public int MinutesBeforeKickoffToEarlyExit { get; init; } = 120;

        // Novo: gap mínimo entre target e preço atual para acionar
        // o fechamento antecipado por falta de convergência.
        public double MinGapToTargetForEarlyExit { get; init; } = 0.08d;
    }
}