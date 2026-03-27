using Arb.Core.Contracts.Events;
using System.Collections.Concurrent;

namespace Arb.Core.Infrastructure.Services
{
    public class SnapshotDedupService
    {
        private readonly ConcurrentDictionary<string, SeenTickState> _lastSeen = new();

        public bool ShouldPublish(OddsTickV1 tick)
        {
            var key = BuildKey(tick);

            var current = new SeenTickState
            {
                OddsDecimal = tick.OddsDecimal,
                Source = tick.Source,
                Ts = tick.Ts
            };

            var existing = _lastSeen.GetOrAdd(key, current);

            if (ReferenceEquals(existing, current))
                return true;

            var changed = existing.OddsDecimal != tick.OddsDecimal;

            if (changed)
            {
                _lastSeen[key] = current;
                return true;
            }

            return false;
        }

        private static string BuildKey(OddsTickV1 tick)
        {
            return $"{tick.Source}|{tick.EventKey}|{tick.MarketType}|{tick.SelectionKey}";
        }

        private sealed class SeenTickState
        {
            public double OddsDecimal { get; set; }
            public string Source { get; set; } = string.Empty;
            public DateTime Ts { get; set; }
        }
    }
}
