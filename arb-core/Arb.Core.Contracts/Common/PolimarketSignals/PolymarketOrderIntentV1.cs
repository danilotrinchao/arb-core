using System.Text.Json.Serialization;

namespace Arb.Core.Contracts.Common.PolimarketSignals
{
    public class PolymarketOrderIntentV1
    {
        [JsonPropertyName("intentId")]
        public string IntentId { get; init; } = string.Empty;

        [JsonPropertyName("observationId")]
        public string ObservationId { get; init; } = string.Empty;

        [JsonPropertyName("observedEventId")]
        public string ObservedEventId { get; init; } = string.Empty;

        [JsonPropertyName("sportKey")]
        public string SportKey { get; init; } = string.Empty;

        [JsonPropertyName("bookmakerKey")]
        public string? BookmakerKey { get; init; }

        [JsonPropertyName("selectionKey")]
        public string SelectionKey { get; init; } = string.Empty;

        [JsonPropertyName("observedTeam")]
        public string ObservedTeam { get; init; } = string.Empty;

        [JsonPropertyName("movementDirection")]
        public string MovementDirection { get; init; } = string.Empty;

        [JsonPropertyName("previousReferencePrice")]
        public decimal PreviousReferencePrice { get; init; }

        [JsonPropertyName("currentReferencePrice")]
        public decimal CurrentReferencePrice { get; init; }

        [JsonPropertyName("targetProbability")]
        public decimal TargetProbability { get; init; }

        [JsonPropertyName("movementPercent")]
        public decimal MovementPercent { get; init; }

        [JsonPropertyName("supportingSources")]
        public int SupportingSources { get; init; }

        [JsonPropertyName("polymarketConditionId")]
        public string PolymarketConditionId { get; init; } = string.Empty;

        [JsonPropertyName("polymarketCatalogId")]
        public string PolymarketCatalogId { get; init; } = string.Empty;

        [JsonPropertyName("polymarketQuestion")]
        public string PolymarketQuestion { get; init; } = string.Empty;

        [JsonPropertyName("polymarketMarketSlug")]
        public string? PolymarketMarketSlug { get; init; }

        [JsonPropertyName("targetSide")]
        public string TargetSide { get; init; } = string.Empty;

        [JsonPropertyName("targetTokenId")]
        public string TargetTokenId { get; init; } = string.Empty;

        [JsonPropertyName("yesTokenId")]
        public string YesTokenId { get; init; } = string.Empty;

        [JsonPropertyName("noTokenId")]
        public string NoTokenId { get; init; } = string.Empty;

        [JsonPropertyName("matchedGammaId")]
        public string? MatchedGammaId { get; init; }

        [JsonPropertyName("matchedGammaStartTime")]
        public string? MatchedGammaStartTime { get; init; }

        [JsonPropertyName("commenceTime")]
        public string? CommenceTime { get; init; }

        [JsonPropertyName("gameStartTime")]
        public string? GameStartTime { get; init; }

        [JsonPropertyName("projectionReasonCode")]
        public string ProjectionReasonCode { get; init; } = string.Empty;

        [JsonPropertyName("generatedAt")]
        public string GeneratedAt { get; init; } = string.Empty;

        // ===== Campos de observabilidade / qualificação =====

        [JsonPropertyName("comparableTargetProbability")]
        public decimal? ComparableTargetProbability { get; init; }

        [JsonPropertyName("initialEdge")]
        public decimal? InitialEdge { get; init; }

        [JsonPropertyName("deltaVsComparableTarget")]
        public decimal? DeltaVsComparableTarget { get; init; }

        [JsonPropertyName("timeToKickoffSeconds")]
        public double? TimeToKickoffSeconds { get; init; }

        [JsonPropertyName("isLongHorizon")]
        public bool IsLongHorizon { get; init; }

        [JsonPropertyName("leaguePolicyCategory")]
        public string LeaguePolicyCategory { get; init; } = "NORMAL";

        [JsonPropertyName("signalQualityScore")]
        public double? SignalQualityScore { get; init; }

        [JsonPropertyName("signalRiskCategory")]
        public string SignalRiskCategory { get; init; } = "MEDIUM";
        [JsonPropertyName("shadowDecision")]
        public string ShadowDecision { get; init; } = "WOULD_PUBLISH";

        [JsonPropertyName("shadowRejectReason")]
        public string? ShadowRejectReason { get; init; }

        [JsonPropertyName("shadowPolicyVersion")]
        public string ShadowPolicyVersion { get; init; } = "SignalShadowPolicyV1";
    }
}