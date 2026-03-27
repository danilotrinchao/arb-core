namespace Arb.Core.Application.Abstractions.MarketData
{
    public class FootballCatalogStreamEvent
    {
        public string StreamEntryId { get; init; } = "0-0";

        public string EventType { get; init; } = string.Empty;

        public string? Version { get; init; }

        public string? GeneratedAt { get; init; }

        public string? SnapshotKey { get; init; }

        public IReadOnlyDictionary<string, string> Fields { get; init; }
            = new Dictionary<string, string>();
    }
}
