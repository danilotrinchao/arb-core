namespace Arb.Core.Infrastructure.Redis.SoccerCatalog
{
    public class FootballCatalogRedisOptions
    {
        public const string SectionName = "FootballCatalogRedis";

        public string ConnectionString { get; set; } = "localhost:6379";

        public string SnapshotKey { get; set; } = "pm:football:quote-eligible:current";

        public string NbaSnapshotKey { get; set; } = "pm:nba:quote-eligible:current";

        public string StreamKey { get; set; } = "pm:events:catalog";

        public string UpdatedEventType { get; set; } = "football.catalog.updated";

        public int ReadCount { get; set; } = 10;

        public int BlockMilliseconds { get; set; } = 3000;

        public int ConnectTimeoutMs { get; set; } = 5000;

        public int SyncTimeoutMs { get; set; } = 15000;

        public int AsyncTimeoutMs { get; set; } = 15000;

        public int KeepAliveSeconds { get; set; } = 30;

        public int PollingIntervalMs { get; set; } = 500;
    }
}
