namespace Arb.Core.Application.Request
{
    public class SourcePollContext
    {
        public string SourceName { get; set; } = string.Empty;

        public string SportKey { get; set; } = string.Empty;

        public DateTime NowUtc { get; set; }

        public DateTime? LastPolledAtUtc { get; set; }

        public DateTime? NearestEventCommenceTimeUtc { get; set; }

        public int DailyCreditsUsed { get; set; }

        public int DailyCreditBudget { get; set; }
    }
}
