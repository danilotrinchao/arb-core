namespace Arb.Core.Application.UseCases.MarketData
{
    public class FootballQuoteCandidate
    {
        public string CatalogId { get; init; } = string.Empty;

        public string ConditionId { get; init; } = string.Empty;

        public string Question { get; init; } = string.Empty;

        public string? MarketSlug { get; init; }

        public string? GameStartTime { get; init; }

        public string SemanticType { get; init; } = string.Empty;

        public string? ReferencedTeam { get; init; }

        // Compatibilidade futebol legado: YES/NO
        public string YesTokenId { get; init; } = string.Empty;

        public string NoTokenId { get; init; } = string.Empty;

        // Novo: suporte a SIDE_A/SIDE_B (NBA H2H)
        public string? SideATokenId { get; init; }

        public string? SideBTokenId { get; init; }

        // Novo: rastreabilidade dos outcome roles disponíveis
        public string? OutcomeRoleA { get; init; }

        public string? OutcomeRoleB { get; init; }

        // Novo: rótulos para SIDE_A/SIDE_B (necessários para H2H)
        // Exemplo: SideALabel = "Team A" para TEAM_VS_TEAM_WINNER
        public string? SideALabel { get; init; }

        public string? SideBLabel { get; init; }

        public string? MatchedGammaId { get; init; }

        public string? MatchedGammaStartTime { get; init; }
    }
}
