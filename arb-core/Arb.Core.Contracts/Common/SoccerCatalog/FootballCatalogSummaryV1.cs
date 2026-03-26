using System.Text.Json.Serialization;

namespace Arb.Core.Contracts.Common.SoccerCatalog
{
    public class FootballCatalogSummaryV1
    {
        [JsonPropertyName("generatedAt")]
        public string GeneratedAt { get; init; } = string.Empty;

        [JsonPropertyName("totalCatalog")]
        public int TotalCatalog { get; init; }

        [JsonPropertyName("sportsPlausible")]
        public int SportsPlausible { get; init; }

        [JsonPropertyName("quoteEligible")]
        public int QuoteEligible { get; init; }

        [JsonPropertyName("tradeEligible")]
        public int TradeEligible { get; init; }
    }
}
