using System.Text.Json.Serialization;

namespace Arb.Core.Infrastructure.External.TheOddsApi.DTOs
{
    public sealed class TheOddsApiBookmakerDto
    {
        [JsonPropertyName("key")]
        public string Key { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("last_update")]
        public DateTime LastUpdate { get; set; }

        [JsonPropertyName("markets")]
        public List<TheOddsApiMarketDto> Markets { get; set; } = new();
    }

}
