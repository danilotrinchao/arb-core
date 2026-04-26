namespace Arb.Core.Application.Abstractions.Persistence
{
    public interface IOrderIntentRejectionRepository
    {
        Task InsertAsync(OrderIntentRejection rejection, CancellationToken ct);
    }

    public record OrderIntentRejection(
        Guid Id,
        string IntentId,
        string SportKey,
        string ObservedTeam,
        string? TargetSide,
        string? PolymarketConditionId,
        string? TargetTokenId,
        string Reason,
        double? EntryMid,
        double? ComparableTarget,
        double? RawTargetProbability,
        double? HeadroomToTarget,
        double? InitialEdge,
        double? DeltaVsComparableTarget,
        double? TimeToKickoffSeconds,
        DateTime? IntentGeneratedAt,
        double? IntentAgeSeconds,
        DateTime CreatedAt,
        string RawPayload
    );
}