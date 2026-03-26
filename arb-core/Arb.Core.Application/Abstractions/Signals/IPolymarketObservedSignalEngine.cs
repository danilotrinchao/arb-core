using Arb.Core.Contracts.Common.PolimarketSignals;
using Arb.Core.Contracts.Common.PolymarketObservation;

namespace Arb.Core.Application.Abstractions.Signals
{
    public interface IPolymarketObservedSignalEngine
    {
        PolymarketOrderIntentV1? TryProcess(
            PolymarketObservedTickV1 tick,
            DateTime utcNow);
    }
}
