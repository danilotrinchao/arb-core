using Arb.Core.Application.Request;

namespace Arb.Core.Application.Abstractions.MarketData
{
    public interface IMarketPollingPolicy
    {
        bool ShouldPoll(SourcePollContext context);
    }
}
