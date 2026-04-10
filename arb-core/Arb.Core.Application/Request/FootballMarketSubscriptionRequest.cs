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

        // Compatibilidade futebol legado
        public string YesTokenId { get; init; } = string.Empty;

        public string NoTokenId { get; init; } = string.Empty;

        // Novo: suporte a SIDE_A/SIDE_B
        public string? SideATokenId { get; init; }

        public string? SideBTokenId { get; init; }

        // Novo: outcome roles para diagnostico
        public string? OutcomeRoleA { get; init; }

        public string? OutcomeRoleB { get; init; }

        public string? MatchedGammaId { get; init; }

        public string? MatchedGammaStartTime { get; init; }

        // Retorna todos os token IDs válidos (YES/NO + SIDE_A/SIDE_B)
        public IReadOnlyCollection<string> TokenIds =>
            new[] { YesTokenId, NoTokenId, SideATokenId, SideBTokenId }
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
    }
}
