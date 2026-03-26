using Arb.Core.Application.Abstractions.MarketData;
using Arb.Core.Application.Request;

namespace Arb.Core.Infrastructure.Policies
{
    public class AdaptivePollingPolicy : IMarketPollingPolicy
    {
        public bool ShouldPoll(SourcePollContext context)
        {
            if (context.DailyCreditsUsed >= context.DailyCreditBudget)
                return false;

            if (context.NearestEventCommenceTimeUtc is null)
                return false;

            var now = context.NowUtc;
            var nearestEvent = context.NearestEventCommenceTimeUtc.Value;

            if (nearestEvent <= now)
                return false;

            var timeToEvent = nearestEvent - now;

            var minInterval = GetMinimumInterval(timeToEvent);

            if (context.LastPolledAtUtc is null)
                return true;

            return (now - context.LastPolledAtUtc.Value) >= minInterval;
        }

        private static TimeSpan GetMinimumInterval(TimeSpan timeToEvent)
        {
            if (timeToEvent > TimeSpan.FromHours(24))
                return TimeSpan.FromHours(6);

            if (timeToEvent > TimeSpan.FromHours(6))
                return TimeSpan.FromHours(1);

            if (timeToEvent > TimeSpan.FromHours(2))
                return TimeSpan.FromMinutes(20);

            return TimeSpan.FromMinutes(5);
        }
    }
}
