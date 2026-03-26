using System.Text.Json.Serialization;

namespace Arb.Core.Contracts.Common.SoccerCatalog
{
    public class FootballCatalogMarketV1
    {
        [JsonPropertyName("catalogId")]
        public string CatalogId { get; init; } = string.Empty;

        [JsonPropertyName("conditionId")]
        public string ConditionId { get; init; } = string.Empty;

        [JsonPropertyName("question")]
        public string Question { get; init; } = string.Empty;

        [JsonPropertyName("marketSlug")]
        public string? MarketSlug { get; init; }

        [JsonPropertyName("gameStartTime")]
        public string? GameStartTime { get; init; }

        [JsonPropertyName("gameStartTimeSource")]
        public string? GameStartTimeSource { get; init; }

        [JsonPropertyName("semanticType")]
        public string SemanticType { get; init; } = string.Empty;

        [JsonPropertyName("referencedTeam")]
        public string? ReferencedTeam { get; init; }

        [JsonPropertyName("yesSemanticMode")]
        public string? YesSemanticMode { get; init; }

        [JsonPropertyName("noSemanticMode")]
        public string? NoSemanticMode { get; init; }

        [JsonPropertyName("matchedGammaId")]
        public string? MatchedGammaId { get; init; }

        [JsonPropertyName("matchedGammaStartTime")]
        public string? MatchedGammaStartTime { get; init; }

        [JsonPropertyName("quoteReasonCode")]
        public string QuoteReasonCode { get; init; } = string.Empty;

        [JsonPropertyName("tradeReasonCode")]
        public string TradeReasonCode { get; init; } = string.Empty;

        [JsonPropertyName("outcomes")]
        public IReadOnlyCollection<FootballCatalogOutcomeV1> Outcomes { get; init; }
            = Array.Empty<FootballCatalogOutcomeV1>();

        [JsonPropertyName("discoveredAt")]
        public string DiscoveredAt { get; init; } = string.Empty;

        [JsonPropertyName("lastSeenAt")]
        public string LastSeenAt { get; init; } = string.Empty;
    }
}
