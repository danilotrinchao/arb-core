using Arb.Core.Application.Abstractions.MarketData;
using Arb.Core.Contracts.Common.SoccerCatalog;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Text.Json;

namespace Arb.Core.Infrastructure.Redis.SoccerCatalog
{
    public class FootballCatalogRedisRepository : IFootballCatalogRedisRepository
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly FootballCatalogRedisConnectionFactory _connectionFactory;
        private readonly FootballCatalogRedisOptions _options;
        private readonly ILogger<FootballCatalogRedisRepository> _logger;

        public FootballCatalogRedisRepository(
            FootballCatalogRedisConnectionFactory connectionFactory,
            IOptions<FootballCatalogRedisOptions> options,
            ILogger<FootballCatalogRedisRepository> logger)
        {
            _connectionFactory = connectionFactory;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<FootballQuoteEligibleSnapshotV1?> GetCurrentSnapshotAsync(
            CancellationToken cancellationToken)
        {
            var db = _connectionFactory.GetDatabase();

            var raw = await db.StringGetAsync(_options.SnapshotKey).WaitAsync(cancellationToken);

            if (raw.IsNullOrEmpty)
            {
                _logger.LogWarning(
                    "Football catalog snapshot key not found. Key={Key}",
                    _options.SnapshotKey);

                return null;
            }

            return JsonSerializer.Deserialize<FootballQuoteEligibleSnapshotV1>(
                raw!,
                JsonOptions);
        }

        // Migrado de ExecuteAsync("XREVRANGE") + TryAsArray para StreamRangeAsync nativo.
        public async Task<string> GetLatestCatalogStreamIdAsync(
            CancellationToken cancellationToken)
        {
            var db = _connectionFactory.GetDatabase();

            var latest = await db.StreamRangeAsync(
                _options.StreamKey,
                minId: "-",
                maxId: "+",
                count: 1,
                messageOrder: Order.Descending).WaitAsync(cancellationToken);

            if (latest.Length == 0)
            {
                return "0-0";
            }

            var latestId = latest[0].Id.ToString();

            return string.IsNullOrWhiteSpace(latestId) ? "0-0" : latestId;
        }

        // Migrado de ExecuteAsync("XREAD", "BLOCK", ...) + ParseXRead para StreamReadAsync nativo.
        // BLOCK removido: segurar a conexão com BLOCK + WaitAsync(cancellationToken) é a causa
        // raiz dos RedisTimeoutException. O polling com delay no hosted service substitui o BLOCK.
        public async Task<IReadOnlyCollection<FootballCatalogStreamEvent>> ReadCatalogEventsAsync(
            string afterStreamId,
            CancellationToken cancellationToken)
        {
            var db = _connectionFactory.GetDatabase();

            var entries = await db.StreamReadAsync(
                _options.StreamKey,
                afterStreamId,
                _options.ReadCount).WaitAsync(cancellationToken);

            if (entries.Length == 0)
            {
                return Array.Empty<FootballCatalogStreamEvent>();
            }

            var output = new List<FootballCatalogStreamEvent>(entries.Length);

            foreach (var entry in entries)
            {
                var fields = entry.Values.ToDictionary(
                    x => x.Name.ToString(),
                    x => x.Value.ToString(),
                    StringComparer.OrdinalIgnoreCase);

                fields.TryGetValue("eventType", out var eventType);
                fields.TryGetValue("version", out var version);
                fields.TryGetValue("generatedAt", out var generatedAt);
                fields.TryGetValue("snapshotKey", out var snapshotKey);

                output.Add(new FootballCatalogStreamEvent
                {
                    StreamEntryId = entry.Id.ToString(),
                    EventType = eventType ?? string.Empty,
                    Version = version,
                    GeneratedAt = generatedAt,
                    SnapshotKey = snapshotKey,
                    Fields = fields
                });
            }

            return output;
        }
    }
}