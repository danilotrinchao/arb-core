using Arb.Core.Application.Abstractions.Settlement;
using Arb.Core.Application.Request;
using Arb.Core.Infrastructure.External.TheOddsApi.DTOs;
using Microsoft.Extensions.Logging;

namespace Arb.Core.Infrastructure.External.TheOddsApi
{
    public sealed class TheOddsApiScoreProvider : IScoreProvider
    {
        private readonly TheOddsApiClient _client;
        private readonly ILogger<TheOddsApiScoreProvider> _logger;

        public TheOddsApiScoreProvider(
            TheOddsApiClient client,
            ILogger<TheOddsApiScoreProvider> logger)
        {
            _client = client;
            _logger = logger;
        }

        public async Task<IReadOnlyList<EventScoreSnapshot>> FetchScoresAsync(
            string sportKey,
            IReadOnlyList<string> eventKeys,
            CancellationToken ct)
        {
            // custo 2 com daysFrom=1, mas filtramos por eventos abertos do esporte
            var raw = await _client.GetScoresAsync(sportKey, daysFrom: 1, ct);

            var set = eventKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);

            var result = raw
                .Where(x => set.Contains(x.Id))
                .Select(Map)
                .ToList();

            _logger.LogInformation(
                "Fetched scores for sport={SportKey}. matchedEvents={Count}",
                sportKey, result.Count);

            return result;
        }

        private static EventScoreSnapshot Map(TheOddsApiScoreEventDto dto)
        {
            int? homeScore = null;
            int? awayScore = null;

            if (dto.Scores is not null)
            {
                var home = dto.Scores.FirstOrDefault(x =>
                    string.Equals(x.Name, dto.HomeTeam, StringComparison.OrdinalIgnoreCase));

                var away = dto.Scores.FirstOrDefault(x =>
                    string.Equals(x.Name, dto.AwayTeam, StringComparison.OrdinalIgnoreCase));

                if (home is not null && int.TryParse(home.Score, out var hs))
                    homeScore = hs;

                if (away is not null && int.TryParse(away.Score, out var ascore))
                    awayScore = ascore;
            }

            return new EventScoreSnapshot
            {
                SportKey = dto.SportKey,
                EventKey = dto.Id,
                HomeTeam = dto.HomeTeam,
                AwayTeam = dto.AwayTeam,
                CommenceTime = dto.CommenceTime,
                Completed = dto.Completed,
                HomeScore = homeScore,
                AwayScore = awayScore,
                WinningSelectionKey = ResolveWinningSelection(homeScore, awayScore, dto.Completed),
                LastUpdate = dto.LastUpdate
            };
        }

        private static string? ResolveWinningSelection(int? homeScore, int? awayScore, bool completed)
        {
            if (!completed || homeScore is null || awayScore is null)
                return null;

            if (homeScore > awayScore) return "HOME";
            if (awayScore > homeScore) return "AWAY";
            return "DRAW";
        }
    }
}
