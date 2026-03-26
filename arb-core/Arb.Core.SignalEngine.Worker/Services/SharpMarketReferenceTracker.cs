using Arb.Core.Contracts.Events;
using Arb.Core.SignalEngine.Worker.Options;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace Arb.Core.SignalEngine.Worker.Services
{
    public class SharpMarketReferenceTracker
    {
        private readonly SignalEngineOptions _options;
        private readonly ConcurrentDictionary<string, MarketReferenceState> _markets = new();

        public SharpMarketReferenceTracker(IOptions<SignalEngineOptions> options)
        {
            _options = options.Value;
        }

        public ReferenceMovementResult Evaluate(OddsTickV1 tick)
        {
            var key = BuildKey(tick);
            var state = _markets.GetOrAdd(key, _ => new MarketReferenceState());

            lock (state.SyncRoot)
            {
                var previousReference = CalculateReference(state.Sources.Values, tick.Ts, out var previousCount);

                state.Sources[tick.Source] = new SourceOddsState
                {
                    OddsDecimal = tick.OddsDecimal,
                    SeenAtUtc = tick.Ts
                };

                var currentReference = CalculateReference(state.Sources.Values, tick.Ts, out var currentCount);

                if (previousReference is null || currentReference is null)
                {
                    return ReferenceMovementResult.InsufficientData(currentCount);
                }

                var movementPercent = ((previousReference.Value - currentReference.Value) / previousReference.Value) * 100.0;
                var isShortening = currentReference.Value < previousReference.Value;

                return new ReferenceMovementResult(
                    HasReference: true,
                    PreviousReferenceOdds: Math.Round(previousReference.Value, 4),
                    CurrentReferenceOdds: Math.Round(currentReference.Value, 4),
                    MovementPercent: Math.Round(movementPercent, 4),
                    IsShortening: isShortening,
                    SourceCount: currentCount
                );
            }
        }

        private double? CalculateReference(
            IEnumerable<SourceOddsState> sourceStates,
            DateTime nowUtc,
            out int validSourceCount)
        {
            var maxAge = TimeSpan.FromMinutes(_options.MaxSourceStalenessMinutes);

            var odds = sourceStates
                .Where(x => x.OddsDecimal > 1.0 && (nowUtc - x.SeenAtUtc) <= maxAge)
                .Select(x => x.OddsDecimal)
                .OrderBy(x => x)
                .ToList();

            validSourceCount = odds.Count;

            if (validSourceCount < _options.MinSourcesForReference)
                return null;

            var mid = odds.Count / 2;

            if (odds.Count % 2 == 0)
                return (odds[mid - 1] + odds[mid]) / 2.0;

            return odds[mid];
        }

        private static string BuildKey(OddsTickV1 tick)
        {
            return $"{tick.EventKey}|{tick.MarketType}|{tick.SelectionKey}";
        }

        private sealed class MarketReferenceState
        {
            public object SyncRoot { get; } = new();
            public Dictionary<string, SourceOddsState> Sources { get; } = new();
        }

        private sealed class SourceOddsState
        {
            public double OddsDecimal { get; set; }
            public DateTime SeenAtUtc { get; set; }
        }

        public sealed record ReferenceMovementResult(
            bool HasReference,
            double? PreviousReferenceOdds,
            double? CurrentReferenceOdds,
            double MovementPercent,
            bool IsShortening,
            int SourceCount)
        {
            public static ReferenceMovementResult InsufficientData(int sourceCount)
                => new(false, null, null, 0, false, sourceCount);
        }
    }
}
