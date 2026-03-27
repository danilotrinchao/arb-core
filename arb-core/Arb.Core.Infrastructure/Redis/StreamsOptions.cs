namespace Arb.Core.Infrastructure.Redis
{
    public class StreamsOptions
    {
        public const string SectionName = "Streams";

        public string OddsTicks { get; init; } = "odds:ticks";
        public string OrderIntents { get; init; } = "orders:intents";
        public string ExecutionReports { get; init; } = "orders:reports";
        public string PolymarketOrderIntents { get; init; } = "polymarket:orders:intents";
    }
}
