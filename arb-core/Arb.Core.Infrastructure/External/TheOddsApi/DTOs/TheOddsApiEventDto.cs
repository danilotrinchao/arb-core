using System.Text.Json.Serialization;

namespace Arb.Core.Infrastructure.External.TheOddsApi.DTOs
{
    public class TheOddsApiEventDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("sport_key")]
        public string SportKey { get; set; } = string.Empty;

        [JsonPropertyName("sport_title")]
        public string? SportTitle { get; set; }

        [JsonPropertyName("commence_time")]
        public DateTime CommenceTime { get; set; }

        [JsonPropertyName("home_team")]
        public string HomeTeam { get; set; } = string.Empty;

        [JsonPropertyName("away_team")]
        public string AwayTeam { get; set; } = string.Empty;

        [JsonPropertyName("bookmakers")]
        public List<TheOddsApiBookmakerDto> Bookmakers { get; set; } = new();
    }
}
