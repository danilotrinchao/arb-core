using System.Text.Json.Serialization;

namespace Arb.Core.Contracts.Common.PolimarketSignals
{
    public class PolymarketOrderIntentV1
    {
        [JsonPropertyName("intentId")]
        public string IntentId { get; init; } = string.Empty;

        [JsonPropertyName("observationId")]
        public string ObservationId { get; init; } = string.Empty;

        [JsonPropertyName("observedEventId")]
        public string ObservedEventId { get; init; } = string.Empty;

        [JsonPropertyName("sportKey")]
        public string SportKey { get; init; } = string.Empty;

        [JsonPropertyName("bookmakerKey")]
        public string? BookmakerKey { get; init; }

        [JsonPropertyName("selectionKey")]
        public string SelectionKey { get; init; } = string.Empty;

        [JsonPropertyName("observedTeam")]
        public string ObservedTeam { get; init; } = string.Empty;

        [JsonPropertyName("movementDirection")]
        public string MovementDirection { get; init; } = string.Empty;

        // Preço de referência anterior em probabilidade implícita (0 a 1)
        // Anteriormente era odd decimal — corrigido no Passo 5 (projector)
        [JsonPropertyName("previousReferencePrice")]
        public decimal PreviousReferencePrice { get; init; }

        // Preço de referência atual em probabilidade implícita (0 a 1)
        // É a mediana dos bookmakers asiáticos convertida para probabilidade
        [JsonPropertyName("currentReferencePrice")]
        public decimal CurrentReferencePrice { get; init; }

        // Alvo de convergência — probabilidade implícita da odd asiática atual
        // O monitor fecha a posição quando mid_price Polymarket >= TargetProbability
        // Igual ao CurrentReferencePrice quando a odd asiática subiu (odds caiu)
        // ou seja: quando o mercado asiático ficou mais confiante no time
        [JsonPropertyName("targetProbability")]
        public decimal TargetProbability { get; init; }

        [JsonPropertyName("movementPercent")]
        public decimal MovementPercent { get; init; }

        [JsonPropertyName("supportingSources")]
        public int SupportingSources { get; init; }

        [JsonPropertyName("polymarketConditionId")]
        public string PolymarketConditionId { get; init; } = string.Empty;

        [JsonPropertyName("polymarketCatalogId")]
        public string PolymarketCatalogId { get; init; } = string.Empty;

        [JsonPropertyName("polymarketQuestion")]
        public string PolymarketQuestion { get; init; } = string.Empty;

        [JsonPropertyName("polymarketMarketSlug")]
        public string? PolymarketMarketSlug { get; init; }

        [JsonPropertyName("targetSide")]
        public string TargetSide { get; init; } = string.Empty;

        [JsonPropertyName("targetTokenId")]
        public string TargetTokenId { get; init; } = string.Empty;

        [JsonPropertyName("yesTokenId")]
        public string YesTokenId { get; init; } = string.Empty;

        [JsonPropertyName("noTokenId")]
        public string NoTokenId { get; init; } = string.Empty;

        [JsonPropertyName("matchedGammaId")]
        public string? MatchedGammaId { get; init; }

        [JsonPropertyName("matchedGammaStartTime")]
        public string? MatchedGammaStartTime { get; init; }

        [JsonPropertyName("gameStartTime")]
        public string? GameStartTime {get; init;}

        [JsonPropertyName("projectionReasonCode")]
        public string ProjectionReasonCode { get; init; } = string.Empty;

        [JsonPropertyName("generatedAt")]
        public string GeneratedAt { get; init; } = string.Empty;

    }
}
