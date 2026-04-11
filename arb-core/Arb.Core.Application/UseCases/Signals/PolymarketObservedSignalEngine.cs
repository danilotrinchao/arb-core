using Arb.Core.Application.Abstractions.Signals;
using Arb.Core.Contracts.Common.PolimarketSignals;
using Arb.Core.Contracts.Common.PolymarketObservation;
using Arb.Core.Infrastructure.Redis.SoccerCatalog;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using System.Linq;

namespace Arb.Core.Application.UseCases.Signals
{
    public class PolymarketObservedSignalEngine : IPolymarketObservedSignalEngine
    {
        private readonly ConcurrentDictionary<string, SignalBucket> _buckets
            = new(StringComparer.OrdinalIgnoreCase);

        private readonly PolymarketObservedSignalOptions _options;
        private readonly ILogger<PolymarketObservedSignalEngine> _logger;

        public PolymarketObservedSignalEngine(
            IOptions<PolymarketObservedSignalOptions> options,
            ILogger<PolymarketObservedSignalEngine> logger)
        {
            _options = options.Value;
            _logger = logger;
        }

        public PolymarketOrderIntentV1? TryProcess(
            PolymarketObservedTickV1 tick,
            DateTime utcNow)
        {
            if (tick is null)
            {
                _logger.LogInformation("SignalEngine reject reason={Reason}", "invalid_tick");
                return null;
            }

            if (string.IsNullOrWhiteSpace(tick.PolymarketConditionId))
            {
                _logger.LogInformation("SignalEngine reject reason={Reason}", "missing_condition_id");
                return null;
            }

            // Valida que pelo menos um par de tokens está disponível:
            // YES/NO (legado) OU SIDE_A/SIDE_B (H2H)
            var hasYesNo = !string.IsNullOrWhiteSpace(tick.YesTokenId) &&
                          !string.IsNullOrWhiteSpace(tick.NoTokenId);

            var hasSideAB = !string.IsNullOrWhiteSpace(tick.SideATokenId) &&
                           !string.IsNullOrWhiteSpace(tick.SideBTokenId);

            if (!hasYesNo && !hasSideAB)
            {
                _logger.LogInformation("SignalEngine reject reason={Reason} conditionId={ConditionId}", "missing_token_ids", tick.PolymarketConditionId);
                return null;
            }

            // ObservedPrice agora em probabilidade (0 a 1) após correção do projector
            // Valores fora do intervalo válido são rejeitados
            if (tick.ObservedPrice <= 0 || tick.ObservedPrice >= 1)
            {
                _logger.LogInformation("SignalEngine reject reason={Reason} conditionId={ConditionId} observedPrice={ObservedPrice}", "observed_price_out_of_range", tick.PolymarketConditionId, tick.ObservedPrice);
                return null;
            }

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
                {
                    _logger.LogInformation("SignalEngine reject reason={Reason} conditionId={ConditionId} supportingSources={Sources} minSources={MinSources}", "insufficient_sources", tick.PolymarketConditionId, supportingSources, _options.MinSources);
                    return null;
                }

                // Mediana dos preços em probabilidade — referência atual do mercado asiático
                var currentReferencePrice = Median(currentPrices);
                var previousReferencePrice = bucket.ReferencePrice;

                bucket.ReferencePrice = currentReferencePrice;

                // Primeira observação — salva referência, não gera intent ainda
                // Na próxima rodada teremos delta para medir
                if (previousReferencePrice is null || previousReferencePrice.Value <= 0)
                {
                    _logger.LogInformation("SignalEngine info={Info} conditionId={ConditionId} currentReferencePrice={CurrentRef}", "first_observation", tick.PolymarketConditionId, currentReferencePrice);
                    return null;
                }

                if (currentReferencePrice == previousReferencePrice.Value)
                {
                    _logger.LogInformation("SignalEngine reject reason={Reason} conditionId={ConditionId} previousRef={PreviousRef} currentRef={CurrentRef}", "no_price_change", tick.PolymarketConditionId, previousReferencePrice.Value, currentReferencePrice);
                    return null;
                }

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
                {
                    _logger.LogInformation("SignalEngine reject reason={Reason} conditionId={ConditionId} movementAbsolute={MovementAbs} threshold={Threshold}", "movement_below_threshold", tick.PolymarketConditionId, movementAbsolute, movementThreshold);
                    return null;
                }

                if (bucket.LastIntentAtUtc is not null && utcNow - bucket.LastIntentAtUtc.Value < TimeSpan.FromSeconds(_options.SignalCooldownSeconds))
                {
                    _logger.LogInformation("SignalEngine reject reason={Reason} conditionId={ConditionId} lastIntentAt={LastIntentAt} cooldownSeconds={Cooldown}", "cooldown_active", tick.PolymarketConditionId, bucket.LastIntentAtUtc, _options.SignalCooldownSeconds);
                    return null;
                }

                // Direção do movimento:
                // Probabilidade SUBIU → asiáticos ficaram mais confiantes no lado observado → ODDS_SHORTENING
                // Probabilidade CAIU  → asiáticos ficaram menos confiantes → ODDS_DRIFTING
                var probabilityRose = currentReferencePrice > previousReferencePrice.Value;

                var movementDirection = probabilityRose
                    ? "ODDS_SHORTENING"
                    : "ODDS_DRIFTING";

                // Resolve target side e token baseado no modelo do tick
                // e na direção do movimento
                ResolveTargetForSignal(
                    tick,
                    probabilityRose,
                    out var targetSide,
                    out var targetTokenId);

                bucket.LastIntentAtUtc = utcNow;

                // Log detalhado de sucesso no gate
                _logger.LogInformation("SignalEngine generate_intent conditionId={ConditionId} supportingSources={Sources} previousReferencePrice={PrevRef} currentReferencePrice={CurrRef} movementAbsolute={MovementAbs} movementThreshold={Threshold} observedSide={ObservedSide} intentSide={IntentSide}",
                    tick.PolymarketConditionId,
                    supportingSources,
                    previousReferencePrice.Value,
                    currentReferencePrice,
                    movementAbsolute,
                    movementThreshold,
                    tick.TargetSide,
                    targetSide);

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
                    GameStartTime = tick.CommenceTime,
                    ProjectionReasonCode = tick.ProjectionReasonCode,
                    GeneratedAt = utcNow.ToString("O")
                };
            }
        }

        /// <summary>
        /// Resolve TargetSide e TargetTokenId para o sinal baseado no modelo do tick
        /// e na direção do movimento.
        /// 
        /// Lógica:
        /// - O tick traz o TargetSide observado (YES/NO ou SIDE_A/SIDE_B)
        /// - Se probabilidade SUBIU, compramos o lado observado
        /// - Se probabilidade CAIU, compramos o lado oposto
        /// 
        /// Exemplo YES/NO:
        ///   Observado: YES, Prob subiu → compra YES
        ///   Observado: YES, Prob caiu → compra NO
        /// 
        /// Exemplo SIDE_A/SIDE_B:
        ///   Observado: SIDE_A, Prob subiu → compra SIDE_A
        ///   Observado: SIDE_A, Prob caiu → compra SIDE_B
        /// </summary>
        private static void ResolveTargetForSignal(
            PolymarketObservedTickV1 tick,
            bool probabilityRose,
            out string targetSide,
            out string targetTokenId)
        {
            // Extrai o side oposto baseado no modelo do tick
            var oppositeForSideA = "SIDE_B";
            var oppositeForYes = "NO";
            var oppositeTokenForSideA = tick.SideBTokenId;
            var oppositeTokenForYes = tick.NoTokenId;

            // Determina se o modelo é YES/NO ou SIDE_A/SIDE_B
            var isSideABModel = !string.IsNullOrWhiteSpace(tick.SideATokenId) &&
                               !string.IsNullOrWhiteSpace(tick.SideBTokenId);

            // Se prob subiu, compramos o lado observado do ticket
            if (probabilityRose)
            {
                targetSide = tick.TargetSide;
                targetTokenId = tick.TargetTokenId;
                return;
            }

            // Se prob caiu, compramos o lado oposto
            if (isSideABModel)
            {
                targetSide = string.Equals(tick.TargetSide, "SIDE_A", StringComparison.OrdinalIgnoreCase)
                    ? oppositeForSideA
                    : "SIDE_A";
                targetTokenId = string.Equals(tick.TargetSide, "SIDE_A", StringComparison.OrdinalIgnoreCase)
                    ? oppositeTokenForSideA
                    : tick.SideATokenId;
                return;
            }

            // Modelo YES/NO
            targetSide = string.Equals(tick.TargetSide, "YES", StringComparison.OrdinalIgnoreCase)
                ? oppositeForYes
                : "YES";
            targetTokenId = string.Equals(tick.TargetSide, "YES", StringComparison.OrdinalIgnoreCase)
                ? oppositeTokenForYes
                : tick.YesTokenId;
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