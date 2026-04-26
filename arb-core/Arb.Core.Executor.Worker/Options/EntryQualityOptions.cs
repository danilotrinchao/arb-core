namespace Arb.Core.Executor.Worker.Options
{
    public class EntryQualityOptions
    {
        public const string SectionName = "EntryQuality";

        // Filtro global mínimo de edge
        public double MinInitialEdgeGlobal { get; init; } = 0.02d;

        // Horizonte "muito antecipado"
        public int LongHorizonMinutes { get; init; } = 2880; // 48h

        // Se estiver >48h do kickoff, exigir edge maior
        public double MinInitialEdgeLongHorizon { get; init; } = 0.03d;

        // Delta = polymarket_entry_price - comparable_target_probability
        // Se ficar acima disso, rejeita globalmente
        public double MaxPositiveDeltaToComparableTargetGlobal { get; init; } = 0.03d;

        // Se estiver >48h e delta acima disso, rejeita
        public double MaxPositiveDeltaToComparableTargetLongHorizon { get; init; } = 0.01d;

        // Liga com política especial
        public string LaLigaSportKey { get; init; } = "soccer_spain_la_liga";
        public string LigueOneSportKey { get; init; } = "soccer_france_ligue_one";

        // Nessas ligas, se delta > 0 já rejeita
        public bool RejectPositiveDeltaForLaLiga { get; init; } = true;
        public bool RejectPositiveDeltaForLigueOne { get; init; } = true;

        // Edge mínimo mais duro por liga problemática
        public double LaLigaMinInitialEdgeGlobal { get; init; } = 0.03d;
        public double LaLigaMinInitialEdgeLongHorizon { get; init; } = 0.04d;

        public double LigueOneMinInitialEdgeGlobal { get; init; } = 0.03d;
        public double LigueOneMinInitialEdgeLongHorizon { get; init; } = 0.04d;
    }
}