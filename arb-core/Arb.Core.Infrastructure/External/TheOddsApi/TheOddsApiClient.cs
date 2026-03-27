using Arb.Core.Infrastructure.External.TheOddsApi.DTOs;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;

namespace Arb.Core.Infrastructure.External.TheOddsApi
{
    public class TheOddsApiClient
    {
        private readonly HttpClient _httpClient;
        private readonly TheOddsApiOptions _options;

        public TheOddsApiClient(HttpClient httpClient, IOptions<TheOddsApiOptions> options)
        {
            _httpClient = httpClient;
            _options = options.Value;

            if (_httpClient.BaseAddress is null)
            {
                _httpClient.BaseAddress = new Uri(_options.BaseUrl);
            }
        }

        public async Task<TheOddsApiFetchResponse> GetOddsAsync(
            string sportKey,
            IReadOnlyList<string> markets,
            IReadOnlyList<string> bookmakers,
            string oddsFormat,
            string dateFormat,
            CancellationToken ct)
        {
            var query = BuildOddsQuery(
                sportKey,
                markets,
                bookmakers,
                oddsFormat,
                dateFormat);

            using var response = await _httpClient.GetAsync(query, ct);

            response.EnsureSuccessStatusCode();

            var events = await response.Content.ReadFromJsonAsync<List<TheOddsApiEventDto>>(cancellationToken: ct)
                         ?? new List<TheOddsApiEventDto>();

            var usage = new TheOddsApiUsageHeaders
            {
                RequestsRemaining = ReadIntHeader(response, "x-requests-remaining"),
                RequestsUsed = ReadIntHeader(response, "x-requests-used"),
                RequestsLast = ReadIntHeader(response, "x-requests-last")
            };

            return new TheOddsApiFetchResponse
            {
                Events = events,
                Usage = usage
            };
        }

        private string BuildOddsQuery(
        string sportKey,
        IReadOnlyList<string> markets,
        IReadOnlyList<string> bookmakers,
        string oddsFormat,
        string dateFormat)
        {
            var marketValue = string.Join(",",
                markets.Where(x => !string.IsNullOrWhiteSpace(x))
                       .Distinct(StringComparer.OrdinalIgnoreCase));

            var bookmakerValue = string.Join(",",
                bookmakers.Where(x => !string.IsNullOrWhiteSpace(x))
                          .Distinct(StringComparer.OrdinalIgnoreCase));

            return $"/v4/sports/{sportKey}/odds" +
                   $"?apiKey={Uri.EscapeDataString(_options.ApiKey)}" +
                   $"&markets={Uri.EscapeDataString(marketValue)}" +
                   $"&bookmakers={Uri.EscapeDataString(bookmakerValue)}" +
                   $"&oddsFormat={Uri.EscapeDataString(oddsFormat)}" +
                   $"&dateFormat={Uri.EscapeDataString(dateFormat)}";
        }

        private static int? ReadIntHeader(HttpResponseMessage response, string headerName)
        {
            if (!response.Headers.TryGetValues(headerName, out var values))
                return null;

            var raw = values.FirstOrDefault();
            return int.TryParse(raw, out var parsed) ? parsed : null;
        }

        public async Task<IReadOnlyList<TheOddsApiScoreEventDto>> GetScoresAsync(
           string sportKey,
           int daysFrom,
           CancellationToken ct)
        {
            var query = $"/v4/sports/{sportKey}/scores" +
                        $"?apiKey={Uri.EscapeDataString(_options.ApiKey)}" +
                        $"&daysFrom={daysFrom}" +
                        $"&dateFormat=iso";

            using var response = await _httpClient.GetAsync(query, ct);
            response.EnsureSuccessStatusCode();

            var events = await response.Content.ReadFromJsonAsync<List<TheOddsApiScoreEventDto>>(cancellationToken: ct)
                         ?? new List<TheOddsApiScoreEventDto>();

            return events;
        }
    }
}
