namespace Arb.Core.Application.Request
{
    public class FootballMarketSubscriptionRequest
    {
        public string CatalogId { get; init; } = string.Empty;

        public string ConditionId { get; init; } = string.Empty;

        public string Question { get; init; } = string.Empty;

        public string? MarketSlug { get; init; }

        public string? GameStartTime { get; init; }

        public string SemanticType { get; init; } = string.Empty;

        public string? ReferencedTeam { get; init; }

        public string YesTokenId { get; init; } = string.Empty;

        public string NoTokenId { get; init; } = string.Empty;

        public string? MatchedGammaId { get; init; }

        public string? MatchedGammaStartTime { get; init; }

        public IReadOnlyCollection<string> TokenIds =>
            new[] { YesTokenId, NoTokenId }
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
    }
}
