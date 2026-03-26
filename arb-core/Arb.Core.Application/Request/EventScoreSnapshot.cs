namespace Arb.Core.Application.Request
{
    public class EventScoreSnapshot
    {
        public string SportKey { get; set; } = string.Empty;
        public string EventKey { get; set; } = string.Empty;
        public string HomeTeam { get; set; } = string.Empty;
        public string AwayTeam { get; set; } = string.Empty;
        public DateTime CommenceTime { get; set; }
        public bool Completed { get; set; }
        public string? WinningSelectionKey { get; set; }   // HOME / AWAY / DRAW
        public int? HomeScore { get; set; }
        public int? AwayScore { get; set; }
        public DateTime? LastUpdate { get; set; }
    }
}
