using Arb.Core.Application.Abstractions.Signals;
using Arb.Core.Contracts.Common.PolimarketSignals;
using Arb.Core.Contracts.Common.PolymarketObservation;
using Arb.Core.Infrastructure.Redis.SoccerCatalog;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace Arb.Core.Application.UseCases.Signals
{
    public class PolymarketObservedSignalEngine : IPolymarketObservedSignalEngine
    {
        private readonly ConcurrentDictionary<string, SignalBucket> _buckets
            = new(StringComparer.OrdinalIgnoreCase);

        private readonly PolymarketObservedSignalOptions _options;

        public PolymarketObservedSignalEngine(
            IOptions<PolymarketObservedSignalOptions> options)
        {
            _options = options.Value;
        }

        public PolymarketOrderIntentV1? TryProcess(
            PolymarketObservedTickV1 tick,
            DateTime utcNow)
        {
            if (tick is null)
                return null;

            if (string.IsNullOrWhiteSpace(tick.PolymarketConditionId))
                return null;

            if (string.IsNullOrWhiteSpace(tick.YesTokenId) ||
                string.IsNullOrWhiteSpace(tick.NoTokenId))
                return null;

            // ObservedPrice agora em probabilidade (0 a 1) após correção do projector
            // Valores fora do intervalo válido são rejeitados
            if (tick.ObservedPrice <= 0 || tick.ObservedPrice >= 1)
                return null;

            var bookmakerKey = string.IsNullOrWhiteSpace(tick.BookmakerKey)
                ? "unknown"
                : tick.BookmakerKey.Trim().ToLowerInvariant();

            var bucketKey = tick.PolymarketConditionId.Trim().ToLowerInvariant();
            var bucket = _buckets.GetOrAdd(bucketKey, _ => new SignalBucket());

            lock (bucket.Sync)
            {
                bucket.LatestByBookmaker[bookmakerKey] = new SourceQuote
                {
                    Price = tick.ObservedPrice,
                    UpdatedAtUtc = utcNow
                };

                // Coleta preços válidos de todas as fontes e ordena para cálculo da mediana
                var currentPrices = bucket.LatestByBookmaker.Values
                    .Select(x => x.Price)
                    .Where(p => p > 0 && p < 1)
                    .OrderBy(p => p)
                    .ToArray();

                var supportingSources = currentPrices.Length;
                if (supportingSources < _options.MinSources)
                    return null;

                // Mediana dos preços em probabilidade — referência atual do mercado asiático
                var currentReferencePrice = Median(currentPrices);
                var previousReferencePrice = bucket.ReferencePrice;

                bucket.ReferencePrice = currentReferencePrice;

                // Primeira observação — salva referência, não gera intent ainda
                // Na próxima rodada teremos delta para medir
                if (previousReferencePrice is null || previousReferencePrice.Value <= 0)
                    return null;

                if (currentReferencePrice == previousReferencePrice.Value)
                    return null;

                // Movimento calculado em pontos absolutos de probabilidade
                // Ex: 0.2273 → 0.2315 = delta de 0.0042 (0.42 pontos percentuais)
                // Threshold MinMovementPct interpretado como pontos percentuais de probabilidade
                // Ex: MinMovementPct = 1.5 significa movimento mínimo de 0.015 em probabilidade
                var movementAbsolute = Math.Abs(
                    currentReferencePrice - previousReferencePrice.Value);

                var movementPercent = Math.Abs(
                    (currentReferencePrice - previousReferencePrice.Value)
                    / previousReferencePrice.Value * 100m);

                // Gate: movimento mínimo em pontos absolutos de probabilidade
                // Converte MinMovementPct de percentual para decimal
                // MinMovementPct = 1.5 → threshold = 0.015
                var movementThreshold = (decimal)_options.MinMovementPct / 100m;

                if (movementAbsolute < movementThreshold)
                    return null;

                if (bucket.LastIntentAtUtc is not null && utcNow - bucket.LastIntentAtUtc.Value < TimeSpan.FromSeconds(_options.SignalCooldownSeconds))
                return null;
                // Direção do movimento:
                // Probabilidade SUBIU → asiáticos ficaram mais confiantes no time → ODDS_SHORTENING
                // Probabilidade CAIU  → asiáticos ficaram menos confiantes → ODDS_DRIFTING
                var probabilityRose = currentReferencePrice > previousReferencePrice.Value;

                var movementDirection = probabilityRose
                    ? "ODDS_SHORTENING"
                    : "ODDS_DRIFTING";

                // Side: compramos YES se a probabilidade subiu (time mais provável de ganhar)
                //       compramos NO  se a probabilidade caiu  (time menos provável de ganhar)
                var targetSide = probabilityRose ? "YES" : "NO";
                var targetTokenId = probabilityRose ? tick.YesTokenId : tick.NoTokenId;

                bucket.LastIntentAtUtc = utcNow;

                return new PolymarketOrderIntentV1
                {
                    IntentId = Guid.NewGuid().ToString("N"),
                    ObservationId = tick.ObservationId,
                    ObservedEventId = tick.ObservedEventId,
                    SportKey = tick.SportKey,
                    BookmakerKey = tick.BookmakerKey,
                    SelectionKey = tick.SelectionKey,
                    ObservedTeam = tick.ObservedTeam,
                    MovementDirection = movementDirection,
                    PreviousReferencePrice = previousReferencePrice.Value,
                    CurrentReferencePrice = currentReferencePrice,

                    // TargetProbability = currentReferencePrice já está em probabilidade
                    // É o alvo que o monitor vai comparar com o mid_price da Polymarket
                    // Quando mid_price Polymarket >= TargetProbability → convergiu → fecha
                    TargetProbability = currentReferencePrice,

                    MovementPercent = Math.Round(
                        movementPercent, 4, MidpointRounding.AwayFromZero),
                    SupportingSources = supportingSources,
                    PolymarketConditionId = tick.PolymarketConditionId,
                    PolymarketCatalogId = tick.PolymarketCatalogId,
                    PolymarketQuestion = tick.PolymarketQuestion,
                    PolymarketMarketSlug = tick.PolymarketMarketSlug,
                    TargetSide = targetSide,
                    TargetTokenId = targetTokenId,
                    YesTokenId = tick.YesTokenId,
                    NoTokenId = tick.NoTokenId,
                    MatchedGammaId = tick.MatchedGammaId,
                    MatchedGammaStartTime = tick.MatchedGammaStartTime,
                    ProjectionReasonCode = tick.ProjectionReasonCode,
                    GeneratedAt = utcNow.ToString("O")
                };
            }
        }

        private static decimal Median(decimal[] sortedValues)
        {
            if (sortedValues.Length == 0)
                return 0m;

            var middle = sortedValues.Length / 2;

            return sortedValues.Length % 2 == 0
                ? (sortedValues[middle - 1] + sortedValues[middle]) / 2m
                : sortedValues[middle];
        }

        private sealed class SignalBucket
        {
            public object Sync { get; } = new();

            public Dictionary<string, SourceQuote> LatestByBookmaker { get; }
                = new(StringComparer.OrdinalIgnoreCase);

            public decimal? ReferencePrice { get; set; }

            public DateTime? LastIntentAtUtc { get; set; }
        }

        private sealed class SourceQuote
        {
            public decimal Price { get; init; }
            public DateTime UpdatedAtUtc { get; init; }
        }
    }
}