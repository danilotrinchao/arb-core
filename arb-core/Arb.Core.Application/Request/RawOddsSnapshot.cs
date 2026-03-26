namespace Arb.Core.Application.Request
{
    public class RawOddsSnapshot
    {
        public string SourceName { get; set; } = string.Empty;

        public string SportKey { get; set; } = string.Empty;

        public string EventKey { get; set; } = string.Empty;

        public string HomeTeam { get; set; } = string.Empty;

        public string AwayTeam { get; set; } = string.Empty;

        public DateTime CommenceTime { get; set; }

        public string MarketType { get; set; } = string.Empty;

        public string SelectionKey { get; set; } = string.Empty;

        public double OddsDecimal { get; set; }

        public string Bookmaker { get; set; } = string.Empty;

        public DateTime LastUpdate { get; set; }
    }
}
