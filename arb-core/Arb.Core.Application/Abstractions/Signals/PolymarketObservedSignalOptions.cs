namespace Arb.Core.Infrastructure.Redis.SoccerCatalog
{
    public class PolymarketObservedSignalOptions
    {
        public const string SectionName = "PolymarketObservedSignal";
        public bool Enabled { get; set; } = true;
        public string InputStreamName { get; set; } = "polymarket:observed:ticks";
        public string OutputStreamName { get; set; } = "polymarket:orders:intents";
        public int ReadCount { get; set; } = 50;
        public int BlockMilliseconds { get; set; } = 3000;
        public decimal MinMovementPct { get; set; } = 1.5m;  // ← era 2.0m
        public int MinSources { get; set; } = 2;
        public int SignalCooldownSeconds { get; set; } = 300; // ← era 30
        public bool StartFromLatest { get; set; } = false;
    }
}
