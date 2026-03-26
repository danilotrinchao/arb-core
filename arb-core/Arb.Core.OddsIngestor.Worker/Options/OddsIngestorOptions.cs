namespace Arb.Core.OddsIngestor.Worker.Options
{
    public class OddsIngestorOptions
    {
        public const string SectionName = "OddsIngestor";

        public int DailyCreditBudget { get; init; } = 10;

        public bool EnableDedup { get; init; } = true;

        public int IdleLoopDelaySeconds { get; init; } = 30;
    }
}
