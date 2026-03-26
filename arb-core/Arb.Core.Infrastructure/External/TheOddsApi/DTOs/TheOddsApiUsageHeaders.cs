namespace Arb.Core.Infrastructure.External.TheOddsApi.DTOs
{
    public sealed class TheOddsApiUsageHeaders
    {
        public int? RequestsRemaining { get; set; }
        public int? RequestsUsed { get; set; }
        public int? RequestsLast { get; set; }
    }
}
