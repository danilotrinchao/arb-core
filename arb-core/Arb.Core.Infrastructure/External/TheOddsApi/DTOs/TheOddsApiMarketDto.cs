using System.Text.Json.Serialization;

namespace Arb.Core.Infrastructure.External.TheOddsApi.DTOs
{
    public class TheOddsApiMarketDto
    {
        [JsonPropertyName("key")]
        public string Key { get; set; } = string.Empty;

        [JsonPropertyName("outcomes")]
        public List<TheOddsApiOutcomeDto> Outcomes { get; set; } = new();
    }
}
