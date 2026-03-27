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
        // Garante que o bot não fica exposto ao resultado do jogo
        // Configurável para ajuste fino durante validação da estratégia
        public int MinutesBeforeKickoffToClose { get; init; } = 30;

        // Intervalo em segundos entre cada varredura do monitor de saída
        // Valor baixo = mais responsivo à convergência, mais chamadas à CLOB
        // Valor alto = menos chamadas, menor chance de capturar o momento exato
        // 30 segundos é um equilíbrio razoável para início
        public int ExitMonitorIntervalSeconds { get; init; } = 30;

        // Idade máxima em minutos do last_known_mid_price para ser considerado confiável
        // Se o último check bem-sucedido foi há mais que isso, o preço é considerado stale
        // e não será usado no fallback de kickoff
        public int MaxPriceAgeMinutes { get; init; } = 10;
    }
}