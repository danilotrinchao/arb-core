namespace Arb.Core.Infrastructure.External.TheOddsApi.DTOs
{
    public sealed class TheOddsApiFetchResponse
    {
        public IReadOnlyList<TheOddsApiEventDto> Events { get; init; } = Array.Empty<TheOddsApiEventDto>();
        public TheOddsApiUsageHeaders Usage { get; init; } = new();
    }
}
