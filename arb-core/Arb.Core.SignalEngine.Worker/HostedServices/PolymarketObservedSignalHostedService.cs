using Arb.Core.Application.Abstractions.Messaging;
using Arb.Core.Application.Abstractions.Signals;
using Arb.Core.Contracts.Common.PolimarketSignals;
using Arb.Core.Contracts.Common.PolymarketObservation;
using Arb.Core.Infrastructure.Redis.SoccerCatalog;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Globalization;
using System.Text.Json;

namespace Arb.Core.SignalEngine.Worker.HostedServices
{
    public class PolymarketObservedSignalHostedService : BackgroundService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly IConnectionMultiplexer _connectionMultiplexer;
        private readonly IStreamPublisher _publisher;
        private readonly IPolymarketObservedSignalEngine _signalEngine;
        private readonly PolymarketObservedSignalOptions _options;
        private readonly ILogger<PolymarketObservedSignalHostedService> _logger;

        public PolymarketObservedSignalHostedService(
            IConnectionMultiplexer connectionMultiplexer,
            IStreamPublisher publisher,
            IPolymarketObservedSignalEngine signalEngine,
            IOptions<PolymarketObservedSignalOptions> options,
            ILogger<PolymarketObservedSignalHostedService> logger)
        {
            _connectionMultiplexer = connectionMultiplexer;
            _publisher = publisher;
            _signalEngine = signalEngine;
            _options = options.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.Enabled)
            {
                _logger.LogInformation("Polymarket observed signal hosted service is disabled");
                return;
            }

            var lastSeenId = _options.StartFromLatest
                ? await GetLatestStreamIdAsync(stoppingToken)
                : "0-0";

            _logger.LogInformation(
                "PolymarketObservedSignal started. Consuming={Input} Publishing={Output} MinMovement={MinMovement}% MinSources={MinSources} StartFromLatest={StartFromLatest} InitialStreamId={InitialStreamId}",
                _options.InputStreamName,
                _options.OutputStreamName,
                _options.MinMovementPct,
                _options.MinSources,
                _options.StartFromLatest,
                lastSeenId);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var events = await ReadEventsAsync(lastSeenId, stoppingToken);

                    if (events.Count == 0)
                    {
                        await Task.Delay(
                            TimeSpan.FromMilliseconds(Math.Max(250, _options.BlockMilliseconds)),
                            stoppingToken);
                        continue;
                    }

                    _logger.LogInformation(
                        "PolymarketObservedSignal read {Count} event(s) from stream {Stream} after {LastSeenId}",
                        events.Count,
                        _options.InputStreamName,
                        lastSeenId);

                    foreach (var evt in events)
                    {
                        lastSeenId = evt.StreamEntryId;

                        if (!evt.Fields.TryGetValue("payload", out var payload) ||
                            string.IsNullOrWhiteSpace(payload))
                        {
                            _logger.LogWarning(
                                "Observed tick stream entry ignored because payload is missing. StreamEntryId={StreamEntryId}",
                                evt.StreamEntryId);
                            continue;
                        }

                        PolymarketObservedTickV1? tick;

                        try
                        {
                            tick = JsonSerializer.Deserialize<PolymarketObservedTickV1>(
                                payload,
                                JsonOptions);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(
                                ex,
                                "Failed to deserialize PolymarketObservedTickV1 from stream. StreamEntryId={StreamEntryId}",
                                evt.StreamEntryId);
                            continue;
                        }

                        if (tick is null)
                        {
                            _logger.LogWarning(
                                "Observed tick stream entry ignored because deserialized payload is null. StreamEntryId={StreamEntryId}",
                                evt.StreamEntryId);
                            continue;
                        }

                        _logger.LogInformation(
                            "Observed tick received. StreamEntryId={StreamEntryId} ObservationId={ObservationId} ConditionId={ConditionId} Team={Team} SelectionKey={SelectionKey} TargetSide={TargetSide} ObservedPrice={ObservedPrice} Bookmaker={Bookmaker}",
                            evt.StreamEntryId,
                            tick.ObservationId,
                            tick.PolymarketConditionId,
                            tick.ObservedTeam,
                            tick.SelectionKey,
                            tick.TargetSide,
                            tick.ObservedPrice.ToString(CultureInfo.InvariantCulture),
                            tick.BookmakerKey ?? "(null)");

                        var intent = _signalEngine.TryProcess(tick, DateTime.UtcNow);

                        if (intent is null)
                        {
                            _logger.LogInformation(
                                "Observed tick processed with no intent generated. ObservationId={ObservationId} ConditionId={ConditionId} Team={Team} SelectionKey={SelectionKey} TargetSide={TargetSide}",
                                tick.ObservationId,
                                tick.PolymarketConditionId,
                                tick.ObservedTeam,
                                tick.SelectionKey,
                                tick.TargetSide);
                            continue;
                        }

                        await PublishIntentAsync(intent, stoppingToken);

                        _logger.LogInformation(
                            "POLYMARKET_ORDER_INTENT published intentId={IntentId} conditionId={ConditionId} side={Side} prevRef={PrevRef} currRef={CurrRef} move={Move}% sources={Sources}",
                            intent.IntentId,
                            intent.PolymarketConditionId,
                            intent.TargetSide,
                            intent.PreviousReferencePrice.ToString(CultureInfo.InvariantCulture),
                            intent.CurrentReferencePrice.ToString(CultureInfo.InvariantCulture),
                            intent.MovementPercent.ToString(CultureInfo.InvariantCulture),
                            intent.SupportingSources);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing polymarket observed ticks");
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                }
            }
        }

        private async Task PublishIntentAsync(
            PolymarketOrderIntentV1 intent,
            CancellationToken ct)
        {
            var payload = JsonSerializer.Serialize(intent);

            var fields = new Dictionary<string, string>
            {
                ["schema"] = "PolymarketOrderIntentV1",
                ["intentId"] = intent.IntentId,
                ["observationId"] = intent.ObservationId,
                ["observedEventId"] = intent.ObservedEventId,
                ["sportKey"] = intent.SportKey,
                ["selectionKey"] = intent.SelectionKey,
                ["observedTeam"] = intent.ObservedTeam,
                ["movementDirection"] = intent.MovementDirection,
                ["previousReferencePrice"] = intent.PreviousReferencePrice.ToString(CultureInfo.InvariantCulture),
                ["currentReferencePrice"] = intent.CurrentReferencePrice.ToString(CultureInfo.InvariantCulture),
                ["movementPercent"] = intent.MovementPercent.ToString(CultureInfo.InvariantCulture),
                ["supportingSources"] = intent.SupportingSources.ToString(CultureInfo.InvariantCulture),
                ["polymarketConditionId"] = intent.PolymarketConditionId,
                ["targetSide"] = intent.TargetSide,
                ["targetTokenId"] = intent.TargetTokenId,
                ["generatedAt"] = intent.GeneratedAt,
                ["payload"] = payload
            };

            await _publisher.PublishAsync(_options.OutputStreamName, fields, ct);
        }

        private async Task<string> GetLatestStreamIdAsync(CancellationToken cancellationToken)
        {
            var db = _connectionMultiplexer.GetDatabase();

            var result = await db.ExecuteAsync(
                "XREVRANGE",
                _options.InputStreamName,
                "+",
                "-",
                "COUNT",
                "1").WaitAsync(cancellationToken);

            if (result.IsNull)
            {
                return "0-0";
            }

            var entries = TryAsArray(result);
            if (entries is null || entries.Length == 0)
            {
                return "0-0";
            }

            var firstEntry = TryAsArray(entries[0]);
            if (firstEntry is null || firstEntry.Length == 0)
            {
                return "0-0";
            }

            var streamId = firstEntry[0].ToString();

            return string.IsNullOrWhiteSpace(streamId) ? "0-0" : streamId!;
        }

        private async Task<IReadOnlyCollection<RedisStreamEvent>> ReadEventsAsync(
            string afterStreamId,
            CancellationToken cancellationToken)
        {
            var db = _connectionMultiplexer.GetDatabase();

              var result = await db.ExecuteAsync(
                "XREAD",
                "COUNT",
                _options.ReadCount.ToString(),
                "STREAMS",
                _options.InputStreamName,
                afterStreamId).WaitAsync(cancellationToken);

            var parsed = ParseXRead(result);

            return parsed;
        }

        private static IReadOnlyCollection<RedisStreamEvent> ParseXRead(RedisResult result)
        {
            var topLevel = TryAsArray(result);
            if (topLevel is null || topLevel.Length == 0)
            {
                return Array.Empty<RedisStreamEvent>();
            }

            var output = new List<RedisStreamEvent>();

            foreach (var streamResult in topLevel)
            {
                var streamParts = TryAsArray(streamResult);
                if (streamParts is null || streamParts.Length < 2)
                {
                    continue;
                }

                var messages = TryAsArray(streamParts[1]);
                if (messages is null || messages.Length == 0)
                {
                    continue;
                }

                foreach (var message in messages)
                {
                    var messageParts = TryAsArray(message);
                    if (messageParts is null || messageParts.Length < 2)
                    {
                        continue;
                    }

                    var entryId = messageParts[0].ToString() ?? "0-0";

                    var fieldValues = TryAsArray(messageParts[1]);
                    if (fieldValues is null || fieldValues.Length == 0)
                    {
                        continue;
                    }

                    var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    for (var i = 0; i < fieldValues.Length - 1; i += 2)
                    {
                        var key = fieldValues[i].ToString();
                        var value = fieldValues[i + 1].ToString();

                        if (string.IsNullOrWhiteSpace(key))
                        {
                            continue;
                        }

                        fields[key!] = value ?? string.Empty;
                    }

                    output.Add(new RedisStreamEvent
                    {
                        StreamEntryId = entryId,
                        Fields = fields
                    });
                }
            }

            return output;
        }

        private static RedisResult[]? TryAsArray(RedisResult result)
        {
            try
            {
                return (RedisResult[])result;
            }
            catch
            {
                return null;
            }
        }

        private class RedisStreamEvent
        {
            public string StreamEntryId { get; init; } = "0-0";

            public IReadOnlyDictionary<string, string> Fields { get; init; } =
                new Dictionary<string, string>();
        }
    }
}