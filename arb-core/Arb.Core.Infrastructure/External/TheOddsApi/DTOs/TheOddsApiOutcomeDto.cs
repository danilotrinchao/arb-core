using System.Text.Json.Serialization;

namespace Arb.Core.Infrastructure.External.TheOddsApi.DTOs
{
    public class TheOddsApiOutcomeDto
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("price")]
        public double Price { get; set; }

        [JsonPropertyName("point")]
        public double? Point { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }
    }
}
