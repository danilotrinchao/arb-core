namespace Arb.Core.Application.Request
{
    public class MarketFetchRequest
    {
        public string SportKey { get; set; } = string.Empty;

        public IReadOnlyList<string> MarketTypes { get; set; } = Array.Empty<string>();

        public IReadOnlyList<string> Bookmakers { get; set; } = Array.Empty<string>();

        public string OddsFormat { get; set; } = "decimal";

        public string DateFormat { get; set; } = "iso";
    }
}
