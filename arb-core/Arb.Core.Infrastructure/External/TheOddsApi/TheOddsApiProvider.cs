using Arb.Core.Application.Abstractions.MarketData;
using Arb.Core.Application.Request;
using Arb.Core.Infrastructure.External.TheOddsApi.DTOs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Arb.Core.Infrastructure.External.TheOddsApi
{
    public class TheOddsApiProvider : IMarketOddsProvider
    {
        private readonly TheOddsApiClient _client;
        private readonly ILogger<TheOddsApiProvider> _logger;
        private readonly TheOddsApiOptions _options;

        public TheOddsApiProvider(
            TheOddsApiClient client,
            ILogger<TheOddsApiProvider> logger,
            IOptions<TheOddsApiOptions> options)
        {
            _client = client;
            _logger = logger;
            _options = options.Value;
        }

        public string SourceName => "theoddsapi";

        public async Task<IReadOnlyList<RawOddsSnapshot>> FetchAsync(
            MarketFetchRequest request,
            CancellationToken ct)
        {
            var response = await _client.GetOddsAsync(
                request.SportKey,
                request.MarketTypes,
                request.Bookmakers,
                request.OddsFormat,
                request.DateFormat,
                ct);

            _logger.LogInformation(
                "TheOddsApi usage -> remaining={Remaining}, used={Used}, lastCost={LastCost}, sport={Sport}",
                response.Usage.RequestsRemaining,
                response.Usage.RequestsUsed,
                response.Usage.RequestsLast,
                request.SportKey);

            var snapshots = MapToSnapshots(response.Events, request);

            _logger.LogInformation(
                "TheOddsApi returned {EventsCount} events and {SnapshotsCount} snapshots for sport={Sport}",
                response.Events.Count,
                snapshots.Count,
                request.SportKey);

            return snapshots;
        }

        private static IReadOnlyList<RawOddsSnapshot> MapToSnapshots(
            IReadOnlyList<TheOddsApiEventDto> events,
            MarketFetchRequest request)
        {
            var result = new List<RawOddsSnapshot>();

            foreach (var ev in events)
            {
                if (ev.Bookmakers is null || ev.Bookmakers.Count == 0)
                    continue;

                foreach (var marketType in request.MarketTypes)
                {
                    var matchingBookmakers = SelectMatchingBookmakers(ev.Bookmakers, request.Bookmakers, marketType);

                    foreach (var bookmaker in matchingBookmakers)
                    {
                        var market = bookmaker.Markets
                            .FirstOrDefault(m => string.Equals(m.Key, marketType, StringComparison.OrdinalIgnoreCase));

                        if (market is null || market.Outcomes.Count == 0)
                            continue;

                        foreach (var outcome in market.Outcomes)
                        {
                            result.Add(new RawOddsSnapshot
                            {
                                SourceName = $"theoddsapi:{bookmaker.Key}",
                                SportKey = ev.SportKey,
                                EventKey = ev.Id,
                                HomeTeam = ev.HomeTeam,
                                AwayTeam = ev.AwayTeam,
                                CommenceTime = ev.CommenceTime,
                                MarketType = market.Key,
                                SelectionKey = NormalizeSelectionKey(outcome.Name, ev.HomeTeam, ev.AwayTeam),
                                OddsDecimal = outcome.Price,
                                Bookmaker = bookmaker.Key,
                                LastUpdate = bookmaker.LastUpdate
                            });
                        }
                    }
                }
            }

            return result;
        }

        private static IReadOnlyList<TheOddsApiBookmakerDto> SelectMatchingBookmakers(
            IReadOnlyList<TheOddsApiBookmakerDto> bookmakers,
            IReadOnlyList<string> preferredBookmakers,
            string marketType)
        {
            var result = new List<TheOddsApiBookmakerDto>();

            foreach (var preferred in preferredBookmakers)
            {
                var match = bookmakers.FirstOrDefault(b =>
                    string.Equals(b.Key, preferred, StringComparison.OrdinalIgnoreCase) &&
                    b.Markets.Any(m => string.Equals(m.Key, marketType, StringComparison.OrdinalIgnoreCase)));

                if (match is not null)
                {
                    result.Add(match);
                }
            }

            return result;
        }

        private static string NormalizeSelectionKey(string outcomeName, string homeTeam, string awayTeam)
        {
            if (string.Equals(outcomeName, homeTeam, StringComparison.OrdinalIgnoreCase))
                return "HOME";

            if (string.Equals(outcomeName, awayTeam, StringComparison.OrdinalIgnoreCase))
                return "AWAY";

            if (string.Equals(outcomeName, "Draw", StringComparison.OrdinalIgnoreCase))
                return "DRAW";

            return outcomeName.Trim().ToUpperInvariant();
        }
    }
}