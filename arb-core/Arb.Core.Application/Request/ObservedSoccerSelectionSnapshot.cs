namespace Arb.Core.Application.Request
{
    public sealed class ObservedSoccerSelectionSnapshot
    {
        public string EventId { get; init; } = string.Empty;

        public string SportKey { get; init; } = string.Empty;

        public string? BookmakerKey { get; init; }

        public string? CommenceTime { get; init; }

        public string ObservedAt { get; init; } = string.Empty;

        public string HomeTeam { get; init; } = string.Empty;

        public string AwayTeam { get; init; } = string.Empty;

        public string SelectionKey { get; init; } = string.Empty;

        public decimal Price { get; init; }
    }
}
