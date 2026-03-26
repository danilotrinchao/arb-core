namespace Arb.Core.Application.Abstractions.Persistence
{
    public interface IPositionRepository
    {
        Task<Guid> CreateOpenAsync(PositionOpen position, CancellationToken ct);

        // Fechamento com preço real e motivo — usado pelo monitor de saída
        Task CloseAsync(
            Guid positionId,
            double pnl,
            DateTime closedAt,
            double? closePrice,
            string exitReason,
            CancellationToken ct);

        // Atualiza o último midpoint consultado com sucesso
        // Chamado pelo monitor a cada consulta bem-sucedida à CLOB
        Task UpdateLastKnownMidPriceAsync(
            Guid positionId,
            double midPrice,
            DateTime checkedAt,
            CancellationToken ct);

        Task<int> CountOpenAsync(CancellationToken ct);
        Task<int> CountOpenPolymarketAsync(CancellationToken ct);

        // Posições legadas para settlement via scores (mantido por compatibilidade,
        // mas não será mais chamado após remoção do fluxo legado no Passo 8)
        Task<IReadOnlyList<OpenPositionForSettlement>> ListEligibleOpenAsync(
            DateTime nowUtc,
            int maxBatchSize,
            CancellationToken ct);

        // Todas as posições Polymarket abertas — usada pelo monitor de saída
        // Sem filtro de data para cobrir jogos de qualquer dia
        Task<IReadOnlyList<OpenPositionForSettlement>> ListOpenPolymarketPositionsAsync(
            CancellationToken ct);

        // Posições Polymarket abertas por data de kickoff — usada pelo WebSocket settlement
        Task<IReadOnlyList<OpenPositionForSettlement>> ListOpenPolymarketPositionsByDateAsync(
            DateOnly date,
            CancellationToken ct);
    }

    public record PositionOpen(
     string IntentId,
     string SportKey,
     string EventKey,
     string HomeTeam,
     string AwayTeam,
     DateTime CommenceTime,
     string MarketType,
     string SelectionKey,
     double Stake,
     double EntryPrice,
     DateTime CreatedAt,
     string? TargetSide = null,
     string? ObservedTeam = null,
     string? PolymarketConditionId = null,
     double? PolymarketEntryPrice = null,
     double? TargetProbability = null,
     string? TargetTokenId = null        // ← estava faltando
    );

    public class OpenPositionForSettlement
    {
        public Guid Id { get; set; }
        public string SportKey { get; set; } = string.Empty;
        public string EventKey { get; set; } = string.Empty;
        public string HomeTeam { get; set; } = string.Empty;
        public string AwayTeam { get; set; } = string.Empty;
        public DateTime CommenceTime { get; set; }
        public string MarketType { get; set; } = string.Empty;
        public string SelectionKey { get; set; } = string.Empty;
        public double Stake { get; set; }
        public double EntryPrice { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? TargetSide { get; set; }
        public string? ObservedTeam { get; set; }
        public string? PolymarketConditionId { get; set; }
        public double? PolymarketEntryPrice { get; set; }
        public double? TargetProbability { get; set; }
        public double? LastKnownMidPrice { get; set; }
        public DateTime? LastPriceCheckedAt { get; set; }

        // TargetTokenId necessário para o monitor consultar o preço correto na CLOB
        // É o token que foi comprado (YES ou NO dependendo do TargetSide)
        public string? TargetTokenId { get; set; }
    }
}