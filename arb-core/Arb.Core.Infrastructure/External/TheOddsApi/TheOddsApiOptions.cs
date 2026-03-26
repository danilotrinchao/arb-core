namespace Arb.Core.Infrastructure.External.TheOddsApi
{
    public class TheOddsApiOptions
    {
        public const string SectionName = "TheOddsApi";

        public string ApiKey { get; set; } = string.Empty;

        public string BaseUrl { get; set; } = "https://api.the-odds-api.com";

        public List<string> SportKeys { get; set; } = new();

        public List<string> Markets { get; set; } = new();

        public List<string> Bookmakers { get; set; } = new();

        public string OddsFormat { get; set; } = "decimal";

        public string DateFormat { get; set; } = "iso";
    }
}
