using Arb.Core.Application.Abstractions.MarketData;
using Arb.Core.Application.Request;
using Arb.Core.Contracts.Events;

namespace Arb.Core.Infrastructure.Normalization
{
    public class OddsTickNormalizer : IOddsNormalizer
    {
    public IReadOnlyList<OddsTickV1> Normalize(
        string sourceName,
        IReadOnlyList<RawOddsSnapshot> snapshots,
        DateTime nowUtc)
        {
            var result = new List<OddsTickV1>();

            foreach (var snapshot in snapshots)
            {
                var league = NormalizeLeague(snapshot.SportKey);

                result.Add(new OddsTickV1(
                    SchemaVersion: "1.0.0",
                    EventId: Guid.NewGuid().ToString("N"),
                    CorrelationId: BuildCorrelationId(snapshot),
                    Ts: nowUtc,
                    Source: snapshot.SourceName,
                    Sport: "soccer",
                    SportKey: snapshot.SportKey,
                    League: league,
                    EventKey: snapshot.EventKey,
                    HomeTeam: snapshot.HomeTeam,
                    AwayTeam: snapshot.AwayTeam,
                    CommenceTime: snapshot.CommenceTime,
                    MarketType: snapshot.MarketType,
                    SelectionKey: snapshot.SelectionKey,
                    OddsDecimal: snapshot.OddsDecimal
                ));
            }

            return result;
        }

        private static string NormalizeLeague(string sportKey)
        {
            return sportKey switch
            {
                "soccer_brazil_campeonato" => "brazil_serie_a",
                _ => sportKey
            };
        }

        private static string BuildCorrelationId(RawOddsSnapshot snapshot)
        {
            return $"{snapshot.SourceName}|{snapshot.EventKey}|{snapshot.MarketType}|{snapshot.SelectionKey}";
        }
    }
}
