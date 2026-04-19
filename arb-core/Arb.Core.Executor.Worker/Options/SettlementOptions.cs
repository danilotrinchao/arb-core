namespace Arb.Core.Executor.Worker.Options
{
    public class SettlementOptions
    {
        public const string SectionName = "Settlement";

        // Intervalo de polling do settlement legado via TheOddsApi scores
        // Mantido por compatibilidade — será removido no Passo 8
        public int PollIntervalSeconds { get; init; } = 60;

        // Tamanho máximo do batch de posições a processar por ciclo
        public int MaxBatchSize { get; init; } = 100;

        // Minutos antes do kickoff para fechar posição Polymarket obrigatoriamente
        public int MinutesBeforeKickoffToClose { get; init; } = 30;

        // Novo: janela anterior ao kickoff para saída antecipada
        // de posições que seguem longe do target
        public int MinutesBeforeKickoffToEarlyExit { get; init; } = 180;

        // Novo: gap mínimo entre comparable target e preço atual
        // para disparar a saída antecipada
        public double MinGapToTargetForEarlyExit { get; init; } = 0.05d;

        // Intervalo em segundos entre cada varredura do monitor de saída
        public int ExitMonitorIntervalSeconds { get; init; } = 30;

        // Idade máxima em minutos do last_known_mid_price para ser considerado confiável
        public int MaxPriceAgeMinutes { get; init; } = 10;
    }
}