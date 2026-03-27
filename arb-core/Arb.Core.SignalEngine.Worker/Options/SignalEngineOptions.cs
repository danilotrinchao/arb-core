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
    }
}
