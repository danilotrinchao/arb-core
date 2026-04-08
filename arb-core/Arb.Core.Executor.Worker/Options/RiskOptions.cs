namespace Arb.Core.Executor.Worker.Options
{
    public class RiskOptions
    {
        public const string SectionName = "Risk";

        // Limite de posições abertas para o fluxo legado
        // Será removido junto com o fluxo legado no Passo 8
        public int MaxOpenPositions { get; init; } = 20;

        // Limite de posições abertas exclusivo para o fluxo Polymarket
        // Separado do legado para que um não bloqueie o outro
        public int MaxPolymarketOpenPositions { get; init; } = 20;

        // Stake fixo em USD por posição Polymarket
        // Na estratégia de convergência o risco por posição é fixo e previsível
        // independente do balance atual do portfolio
        public double PolymarketFixedStakeUsd { get; init; } = 10.0;

        // Mantido para o fluxo legado — será removido no Passo 8
        public double MaxStakePercentPerTrade { get; init; } = 2.0;
        public double MaxDailyLossPercent { get; init; } = 10.0;
    }
}