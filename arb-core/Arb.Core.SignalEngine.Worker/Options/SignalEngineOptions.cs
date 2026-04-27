namespace Arb.Core.SignalEngine.Worker.Options
{
    public sealed class SignalEngineOptions
    {
        public const string SectionName = "SignalEngine";

        public double MinMovementPercent { get; init; } = 2.0;

        public double DefaultStake { get; init; } = 10.0;

        public string StrategyName { get; init; } = "SharpReferenceMovementV1";

        public string Venue { get; init; } = "paper";

        public bool RequireShorteningOdds { get; init; } = true;

        public int MinSourcesForReference { get; init; } = 2;

        public int MaxSourceStalenessMinutes { get; init; } = 120;
        public int LongHorizonMinutes { get; init; } = 2880;
        public string RestrictedLeagueKeysCsv { get; init; } = "soccer_spain_la_liga,soccer_france_ligue_one";
        public string PreferredLeagueKeysCsv { get; init; } = "soccer_brazil_campeonato,soccer_uefa_champs_league";
        public double ScoreBonusForNonPositiveDelta { get; init; } = 10;
        public double ScorePenaltyForLongHorizon { get; init; } = 10;
        public double ScorePenaltyForRestrictedLeague { get; init; } = 10;
    }
}
