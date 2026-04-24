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

        // Início da janela máxima onde early exit pode ser considerado
        // Mantemos 180 como teto, mas a decisão efetiva agora depende do regime da posição
        public int MinutesBeforeKickoffToEarlyExit { get; init; } = 180;

        // Gap mínimo entre comparable target e preço atual para considerar early exit
        public double MinGapToTargetForEarlyExit { get; init; } = 0.05d;

        // Movimento adverso mínimo versus entrada para classificar posição como "adverse"
        // Ex.: -0.01 = preço caiu 1 ponto percentual
        public double MinAdverseMoveToEarlyExit { get; init; } = 0.04d;

        // Faixa neutra/flat perto da entrada
        // Ex.: 0.005 = até meio ponto percentual para cima/baixo ainda é flat
        public double FlatMoveToleranceForEarlyExit { get; init; } = 0.005d;

        // Acima disso a posição é considerada favorável e deve ser protegida de early exit
        public double ProtectProfitableMoveFromEarlyExit { get; init; } = 0.015d;

        // Janela máxima para permitir early exit em posições adversas
        // Evita realizar prejuízo cedo demais
        public int NegativeEarlyExitWindowMinutes { get; init; } = 90;

        // Janela máxima para permitir early exit em posições flat
        public int FlatEarlyExitWindowMinutes { get; init; } = 75;

        // Janela máxima para permitir early exit em posições levemente positivas
        // Só faz sentido já mais perto do kickoff
        public int SlightlyPositiveEarlyExitWindowMinutes { get; init; } = 60;

        // Intervalo em segundos entre cada varredura do monitor de saída
        public int ExitMonitorIntervalSeconds { get; init; } = 30;

        // Idade máxima em minutos do last_known_mid_price para ser considerado confiável
        public int MaxPriceAgeMinutes { get; init; } = 10;
    }
}