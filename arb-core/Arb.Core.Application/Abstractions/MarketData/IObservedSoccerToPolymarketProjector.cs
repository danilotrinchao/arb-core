using Arb.Core.Application.Request;
using Arb.Core.Application.UseCases.MarketData;
using Arb.Core.Contracts.Common.PolymarketObservation;

namespace Arb.Core.Application.Abstractions.MarketData
{
    public interface IObservedSoccerToPolymarketProjector
    {
        IReadOnlyCollection<PolymarketObservedTickV1> Project(
             IReadOnlyCollection<ObservedSoccerSelectionSnapshot> observations,
             IReadOnlyCollection<FootballQuoteCandidate> activeCandidates);
    }
}
