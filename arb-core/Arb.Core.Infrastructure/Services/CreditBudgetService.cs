using System.Collections.Concurrent;

namespace Arb.Core.Infrastructure.Services
{
    public class CreditBudgetService
    {
        private readonly ConcurrentDictionary<string, DailyUsageState> _usageBySource = new();

        public int GetUsedToday(string sourceName)
        {
            var state = GetOrCreate(sourceName);
            ResetIfNeeded(state);
            return state.UsedToday;
        }

        public void RegisterUsage(string sourceName, int creditsUsed)
        {
            var state = GetOrCreate(sourceName);
            ResetIfNeeded(state);

            if (creditsUsed < 0)
                creditsUsed = 0;

            state.UsedToday += creditsUsed;
            state.LastUpdatedUtc = DateTime.Now;
        }

        public bool HasBudget(string sourceName, int dailyBudget)
        {
            var state = GetOrCreate(sourceName);
            ResetIfNeeded(state);

            return state.UsedToday < dailyBudget;
        }

        private DailyUsageState GetOrCreate(string sourceName)
        {
            return _usageBySource.GetOrAdd(sourceName, _ => new DailyUsageState
            {
                DateUtc = DateOnly.FromDateTime(DateTime.UtcNow),
                UsedToday = 0,
                LastUpdatedUtc = DateTime.Now
            });
        }

        private static void ResetIfNeeded(DailyUsageState state)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            if (state.DateUtc != today)
            {
                state.DateUtc = today;
                state.UsedToday = 0;
                state.LastUpdatedUtc = DateTime.Now;
            }
        }

        private sealed class DailyUsageState
        {
            public DateOnly DateUtc { get; set; }
            public int UsedToday { get; set; }
            public DateTime LastUpdatedUtc { get; set; }
        }
    }
}
