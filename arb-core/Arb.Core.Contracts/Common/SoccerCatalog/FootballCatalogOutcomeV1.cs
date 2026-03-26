using System.Text.Json.Serialization;

namespace Arb.Core.Contracts.Common.SoccerCatalog
{
    public sealed class FootballCatalogOutcomeV1
    {
        [JsonPropertyName("tokenId")]
        public string TokenId { get; init; } = string.Empty;

        [JsonPropertyName("outcomeLabel")]
        public string OutcomeLabel { get; init; } = string.Empty;

        [JsonPropertyName("normalizedOutcomeKey")]
        public string? NormalizedOutcomeKey { get; init; }

        [JsonPropertyName("binaryOutcomeRole")]
        public string? BinaryOutcomeRole { get; init; }

        [JsonPropertyName("price")]
        public decimal? Price { get; init; }

        [JsonPropertyName("winner")]
        public bool? Winner { get; init; }
    }
}
