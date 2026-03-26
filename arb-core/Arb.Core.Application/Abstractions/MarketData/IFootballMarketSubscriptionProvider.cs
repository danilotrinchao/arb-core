using Arb.Core.Application.Request;

namespace Arb.Core.Application.Abstractions.MarketData
{
    public interface IFootballMarketSubscriptionProvider
    {
        IReadOnlyCollection<FootballMarketSubscriptionRequest> GetCurrentSubscriptions();

        IReadOnlyCollection<string> GetCurrentTokenIds();
    }
}
