namespace Arb.Core.Application.Abstractions.Persistence
{
    public interface IPositionAnalyticsRepository
    {
        Task InsertClosureAnalyticsAsync(
            PositionClosureAnalytics analytics,
            CancellationToken ct);
    }

    public record PositionClosureAnalytics(
        Guid PositionId,
        Guid IntentId,
        string SportKey,
        string EventKey,
        string MarketType,
        string SelectionKey,
        string? TargetSide,
        string? ObservedTeam,
        string? PolymarketConditionId,
        string? TargetTokenId,
        DateTime CommenceTime,
        DateTime OpenedAt,
        DateTime ClosedAt,
        double Stake,
        double EntryPrice,
        double? PolymarketEntryPrice,
        double? ClosePrice,
        double? PnL,
        string ExitReason,
        double? TargetProbability,
        double? LastKnownMidPrice,
        DateTime? LastPriceCheckedAt,
        double TimeToKickoffAtEntrySeconds,
        double TimeToKickoffAtCloseSeconds,
        bool HadMissingMidpointAtClose,
        bool UsedLastKnownMidPriceFallback
    );
}