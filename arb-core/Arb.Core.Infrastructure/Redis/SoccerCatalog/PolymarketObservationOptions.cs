namespace Arb.Core.Infrastructure.Redis.SoccerCatalog
{
    public class PolymarketObservationOptions
    {
        public const string SectionName = "PolymarketObservation";

        public string StreamName { get; set; } = "polymarket:observed:ticks";

        public bool Enabled { get; set; } = true;
    }
}
