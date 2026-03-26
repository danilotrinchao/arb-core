using System.Text.Json.Serialization;

namespace Arb.Core.Contracts.Common.SoccerCatalog
{
    public class FootballQuoteEligibleSnapshotV1
    {
        [JsonPropertyName("snapshotType")]
        public string SnapshotType { get; init; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; init; } = string.Empty;

        [JsonPropertyName("generatedAt")]
        public string GeneratedAt { get; init; } = string.Empty;

        [JsonPropertyName("summary")]
        public FootballCatalogSummaryV1 Summary { get; init; } = new();

        [JsonPropertyName("markets")]
        public IReadOnlyCollection<FootballCatalogMarketV1> Markets { get; init; }
            = Array.Empty<FootballCatalogMarketV1>();
    }
}
