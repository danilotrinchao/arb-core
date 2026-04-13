namespace Arb.Core.Application.Abstractions.Persistence
{
    public interface IPositionRepository
    {
        Task<Guid> CreateOpenAsync(PositionOpen position, CancellationToken ct);

        Task CloseAsync(
            Guid positionId,
            double pnl,
            DateTime closedAt,
            double? closePrice,
            string exitReason,
            CancellationToken ct);

        Task UpdateLastKnownMidPriceAsync(
            Guid positionId,
            double midPrice,
            DateTime checkedAt,
            CancellationToken ct);

        Task<int> CountOpenAsync(CancellationToken ct);
        Task<int> CountOpenPolymarketAsync(CancellationToken ct);

        Task<IReadOnlyList<OpenPositionForSettlement>> ListEligibleOpenAsync(
            DateTime nowUtc,
            int maxBatchSize,
            CancellationToken ct);

        Task<IReadOnlyList<OpenPositionForSettlement>> ListOpenPolymarketPositionsAsync(
            CancellationToken ct);

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
        string? TargetTokenId = null
    );

    public class OpenPositionForSettlement
    {
        public Guid Id { get; set; }

        // Intent real persistido na tabela positions.intent_id
        // Necessário para analytics e execution report corretos
        public Guid IntentId { get; set; }

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
        public string? TargetTokenId { get; set; }
    }
}