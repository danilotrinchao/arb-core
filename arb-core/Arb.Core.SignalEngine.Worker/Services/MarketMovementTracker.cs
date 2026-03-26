using Arb.Core.Contracts.Events;
using System.Collections.Concurrent;

namespace Arb.Core.SignalEngine.Worker.Services
{
    public sealed class MarketMovementTracker
    {
        private readonly ConcurrentDictionary<string, SeenOddsState> _state = new();

        public MovementResult Evaluate(OddsTickV1 tick)
        {
            var key = BuildKey(tick);

            var current = new SeenOddsState
            {
                OddsDecimal = tick.OddsDecimal,
                SeenAtUtc = tick.Ts
            };

            var previous = _state.GetOrAdd(key, current);

            // primeira vez vendo esse mercado/seleção
            if (ReferenceEquals(previous, current))
            {
                return MovementResult.NoPrevious();
            }

            if (previous.OddsDecimal <= 0)
            {
                _state[key] = current;
                return MovementResult.NoPrevious();
            }

            var movementPercent = ((previous.OddsDecimal - tick.OddsDecimal) / previous.OddsDecimal) * 100.0;
            var isShortening = tick.OddsDecimal < previous.OddsDecimal;

            _state[key] = current;

            return new MovementResult(
                HasPrevious: true,
                PreviousOdds: previous.OddsDecimal,
                CurrentOdds: tick.OddsDecimal,
                MovementPercent: Math.Round(movementPercent, 4),
                IsShortening: isShortening
            );
        }

        private static string BuildKey(OddsTickV1 tick)
        {
            return $"{tick.Source}|{tick.EventKey}|{tick.MarketType}|{tick.SelectionKey}";
        }

        public record MovementResult(
            bool HasPrevious,
            double? PreviousOdds,
            double CurrentOdds,
            double MovementPercent,
            bool IsShortening)
        {
            public static MovementResult NoPrevious()
                => new(false, null, 0, 0, false);
        }

        private class SeenOddsState
        {
            public double OddsDecimal { get; set; }
            public DateTime SeenAtUtc { get; set; }
        }
    }
}
