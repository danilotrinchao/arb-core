using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

        public string YesTokenId { get; init; } = string.Empty;

        public string NoTokenId { get; init; } = string.Empty;

        public string? MatchedGammaId { get; init; }

        public string? MatchedGammaStartTime { get; init; }
    }
}
