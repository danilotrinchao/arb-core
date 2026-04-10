using System.Text.Json.Serialization;

namespace Arb.Core.Contracts.Common.PolymarketObservation
{
    public class PolymarketObservedTickV1
    {
        [JsonPropertyName("observationId")]
        public string ObservationId { get; init; } = string.Empty;

        [JsonPropertyName("observedEventId")]
        public string ObservedEventId { get; init; } = string.Empty;

        [JsonPropertyName("sportKey")]
        public string SportKey { get; init; } = string.Empty;

        [JsonPropertyName("bookmakerKey")]
        public string? BookmakerKey { get; init; }

        [JsonPropertyName("commenceTime")]
        public string? CommenceTime { get; init; }

        [JsonPropertyName("observedAt")]
        public string ObservedAt { get; init; } = string.Empty;

        [JsonPropertyName("selectionKey")]
        public string SelectionKey { get; init; } = string.Empty;

        [JsonPropertyName("observedTeam")]
        public string ObservedTeam { get; init; } = string.Empty;

        [JsonPropertyName("observedPrice")]
        public decimal ObservedPrice { get; init; }

        [JsonPropertyName("polymarketConditionId")]
        public string PolymarketConditionId { get; init; } = string.Empty;

        [JsonPropertyName("polymarketCatalogId")]
        public string PolymarketCatalogId { get; init; } = string.Empty;

        [JsonPropertyName("polymarketMarketSlug")]
        public string? PolymarketMarketSlug { get; init; }

        [JsonPropertyName("polymarketQuestion")]
        public string PolymarketQuestion { get; init; } = string.Empty;

        [JsonPropertyName("polymarketSemanticType")]
        public string PolymarketSemanticType { get; init; } = string.Empty;

        [JsonPropertyName("polymarketReferencedTeam")]
        public string? PolymarketReferencedTeam { get; init; }

        [JsonPropertyName("targetSide")]
        public string TargetSide { get; init; } = "YES";

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

        [JsonPropertyName("projectionReasonCode")]
        public string ProjectionReasonCode { get; init; } = string.Empty;
    }
}
