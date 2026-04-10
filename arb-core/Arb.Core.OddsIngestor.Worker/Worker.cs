using Arb.Core.Application.Abstractions.MarketData;
using Arb.Core.Application.Abstractions.Messaging;
using Arb.Core.Application.Request;
using Arb.Core.Application.UseCases.MarketData;
using Arb.Core.Infrastructure.External.TheOddsApi;
using Arb.Core.Infrastructure.Redis;
using Arb.Core.Infrastructure.Redis.SoccerCatalog;
using Arb.Core.Infrastructure.Services;
using Arb.Core.OddsIngestor.Worker.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Arb.Core.OddsIngestor.Worker
{
    public class Worker : BackgroundService
    {
        private const string TeamToWinSemanticType = "TEAM_TO_WIN_YES_NO";
        private const string TeamVsTeamSemanticType = "TEAM_VS_TEAM_WINNER";

        private readonly ILogger<Worker> _logger;
        private readonly IStreamPublisher _publisher;
        private readonly IOddsNormalizer _normalizer;
        private readonly IMarketPollingPolicy _pollingPolicy;
        private readonly CreditBudgetService _creditBudgetService;
        private readonly SnapshotDedupService _snapshotDedupService;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IObservedSoccerToPolymarketProjector _observedSoccerToPolymarketProjector;
        private readonly IFootballMarketRegistry _footballMarketRegistry;

        private readonly StreamsOptions _streams;
        private readonly TheOddsApiOptions _theOddsApiOptions;
        private readonly OddsIngestorOptions _oddsIngestorOptions;
        private readonly PolymarketObservationOptions _polymarketObservationOptions;

        private readonly ConcurrentDictionary<string, SportPollState> _pollStateBySport = new();

        public Worker(
            ILogger<Worker> logger,
            IStreamPublisher publisher,
            IOddsNormalizer normalizer,
            IMarketPollingPolicy pollingPolicy,
            CreditBudgetService creditBudgetService,
            SnapshotDedupService snapshotDedupService,
            IServiceScopeFactory scopeFactory,
            IObservedSoccerToPolymarketProjector observedSoccerToPolymarketProjector,
            IFootballMarketRegistry footballMarketRegistry,
            IOptions<StreamsOptions> streamsOptions,
            IOptions<TheOddsApiOptions> theOddsApiOptions,
            IOptions<OddsIngestorOptions> oddsIngestorOptions,
            IOptions<PolymarketObservationOptions> polymarketObservationOptions)
        {
            _logger = logger;
            _publisher = publisher;
            _normalizer = normalizer;
            _pollingPolicy = pollingPolicy;
            _creditBudgetService = creditBudgetService;
            _snapshotDedupService = snapshotDedupService;
            _scopeFactory = scopeFactory;
            _observedSoccerToPolymarketProjector = observedSoccerToPolymarketProjector;
            _footballMarketRegistry = footballMarketRegistry;
            _streams = streamsOptions.Value;
            _theOddsApiOptions = theOddsApiOptions.Value;
            _oddsIngestorOptions = oddsIngestorOptions.Value;
            _polymarketObservationOptions = polymarketObservationOptions.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var sportKeys = _theOddsApiOptions.SportKeys
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var markets = _theOddsApiOptions.Markets
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var bookmakers = _theOddsApiOptions.Bookmakers
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (sportKeys.Length == 0)
            {
                _logger.LogWarning("No sport keys configured in TheOddsApi options. OddsIngestor will stay idle.");
                return;
            }

            _logger.LogInformation(
                "OddsIngestor started. Source=TheOddsApi Sports={Sports} Markets={Markets} Bookmakers={Books} DailyBudget={Budget} PolymarketObservedStream={ObservedStream} Enabled={ObservedEnabled}",
                string.Join(",", sportKeys),
                string.Join(",", markets),
                string.Join(",", bookmakers),
                _oddsIngestorOptions.DailyCreditBudget,
                _polymarketObservationOptions.StreamName,
                _polymarketObservationOptions.Enabled);

            while (!stoppingToken.IsCancellationRequested)
            {
                foreach (var sportKey in sportKeys)
                {
                    try
                    {
                        await ProcessSportAsync(sportKey, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing sport {SportKey}", sportKey);
                    }
                }

                await Task.Delay(
                    TimeSpan.FromSeconds(_oddsIngestorOptions.IdleLoopDelaySeconds),
                    stoppingToken);
            }
        }

        private async Task ProcessSportAsync(string sportKey, CancellationToken ct)
        {
            var state = _pollStateBySport.GetOrAdd(sportKey, _ => new SportPollState());

            var now = DateTime.UtcNow;
            var sourceName = "theoddsapi";

            bool shouldPoll;

            if (state.LastPolledAtUtc is null || state.NearestEventCommenceTimeUtc is null)
            {
                shouldPoll = _creditBudgetService.HasBudget(
                    sourceName,
                    _oddsIngestorOptions.DailyCreditBudget);
            }
            else
            {
                var context = new SourcePollContext
                {
                    SourceName = sourceName,
                    SportKey = sportKey,
                    NowUtc = now,
                    LastPolledAtUtc = state.LastPolledAtUtc,
                    NearestEventCommenceTimeUtc = state.NearestEventCommenceTimeUtc,
                    DailyCreditsUsed = _creditBudgetService.GetUsedToday(sourceName),
                    DailyCreditBudget = _oddsIngestorOptions.DailyCreditBudget
                };

                shouldPoll = _pollingPolicy.ShouldPoll(context);
            }

            if (!shouldPoll)
            {
                _logger.LogDebug(
                    "Skipping poll for sport={SportKey} due to polling policy or budget",
                    sportKey);
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var provider = scope.ServiceProvider.GetRequiredService<IMarketOddsProvider>();

            var request = new MarketFetchRequest
            {
                SportKey = sportKey,
                MarketTypes = _theOddsApiOptions.Markets
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                Bookmakers = _theOddsApiOptions.Bookmakers
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                OddsFormat = _theOddsApiOptions.OddsFormat,
                DateFormat = _theOddsApiOptions.DateFormat
            };

            var snapshots = await provider.FetchAsync(request, ct);

            state.LastPolledAtUtc = now;
            state.NearestEventCommenceTimeUtc = snapshots.Count > 0
                ? snapshots.Min(x => x.CommenceTime)
                : null;

            if (snapshots.Count > 0)
            {
                _creditBudgetService.RegisterUsage(sourceName, 1);
            }

            if (snapshots.Count == 0)
            {
                _logger.LogInformation("No snapshots returned for sport={SportKey}", sportKey);
                return;
            }

            var ticks = _normalizer.Normalize(provider.SourceName, snapshots, now);

            var published = 0;
            var skipped = 0;

            foreach (var tick in ticks)
            {
                var shouldPublishTick =
                    !_oddsIngestorOptions.EnableDedup ||
                    _snapshotDedupService.ShouldPublish(tick);

                if (!shouldPublishTick)
                {
                    skipped++;
                    continue;
                }

                var payload = JsonSerializer.Serialize(tick);

                var fields = new Dictionary<string, string>
                {
                    ["schema"] = "OddsTickV1",
                    ["schemaVersion"] = tick.SchemaVersion,
                    ["eventId"] = tick.EventId,
                    ["correlationId"] = tick.CorrelationId,
                    ["ts"] = tick.Ts.ToString("O"),
                    ["source"] = tick.Source,
                    ["sport"] = tick.Sport,
                    ["league"] = tick.League,
                    ["eventKey"] = tick.EventKey,
                    ["marketType"] = tick.MarketType,
                    ["selectionKey"] = tick.SelectionKey,
                    ["oddsDecimal"] = tick.OddsDecimal.ToString(CultureInfo.InvariantCulture),
                    ["payload"] = payload
                };

                await _publisher.PublishAsync(_streams.OddsTicks, fields, ct);
                published++;
            }

            var projectedPublished = 0;

            if (_polymarketObservationOptions.Enabled)
            {
                var observedBuild = BuildObservedSoccerSelectionSnapshots(
                    snapshots.Cast<object>().ToArray(),
                    sportKey,
                    now);

                var observations = observedBuild.Observations;
                var activeSlice = BuildActivePolymarketCandidateSlice(observations);
                var projected = _observedSoccerToPolymarketProjector.Project(
                    observations,
                    activeSlice.ActiveCandidates);

                foreach (var item in projected)
                {
                    var payload = JsonSerializer.Serialize(item);

                    var fields = new Dictionary<string, string>
                    {
                        ["schema"] = "PolymarketObservedTickV1",
                        ["observationId"] = item.ObservationId,
                        ["observedEventId"] = item.ObservedEventId,
                        ["sportKey"] = item.SportKey,
                        ["bookmakerKey"] = item.BookmakerKey ?? string.Empty,
                        ["selectionKey"] = item.SelectionKey,
                        ["observedTeam"] = item.ObservedTeam,
                        ["observedPrice"] = item.ObservedPrice.ToString(CultureInfo.InvariantCulture),
                        ["polymarketConditionId"] = item.PolymarketConditionId,
                        ["targetSide"] = item.TargetSide,
                        ["targetTokenId"] = item.TargetTokenId,
                        ["yesTokenId"] = item.YesTokenId,
                        ["noTokenId"] = item.NoTokenId,
                        ["observedAt"] = item.ObservedAt,
                        ["projectionReasonCode"] = item.ProjectionReasonCode,
                        ["payload"] = payload
                    };

                    await _publisher.PublishAsync(
                        _polymarketObservationOptions.StreamName,
                        fields,
                        ct);

                    projectedPublished++;
                }

                if (projectedPublished > 0)
                {
                    _logger.LogInformation(
                        "Projected observed ticks to Polymarket. Sport={SportKey} RawSnapshots={RawCount} BuiltObservations={ObservationCount} ActiveCandidates={ActiveCandidates} Projected={ProjectedCount} Stream={Stream}",
                        sportKey,
                        observedBuild.RawSnapshots.ToString(),
                        observations.Count(),
                        activeSlice.ActiveCandidates.Count(),
                        projectedPublished,
                        _polymarketObservationOptions.StreamName);
                }
                else
                {
                    LogProjectionDiagnostics(
                        sportKey,
                        observedBuild,
                        observations,
                        activeSlice);
                }
            }

            _logger.LogInformation(
                "Processed sport={SportKey} snapshots={Snapshots} ticks={Ticks} published={Published} skipped={Skipped} projected={Projected} budgetUsedToday={Used}",
                sportKey,
                snapshots.Count,
                ticks.Count,
                published,
                skipped,
                projectedPublished,
                _creditBudgetService.GetUsedToday(sourceName));
        }

        private void LogProjectionDiagnostics(
            string sportKey,
            ObservedBuildResult observedBuild,
            IReadOnlyCollection<ObservedSoccerSelectionSnapshot> observations,
            ActiveSliceDiagnostics activeSlice)
        {
            _logger.LogWarning(
                "Polymarket projection diagnostics. Sport={SportKey} RawSnapshots={RawSnapshots} BuiltObservations={BuiltObservations} MissingEventId={MissingEventId} MissingHomeTeam={MissingHomeTeam} MissingAwayTeam={MissingAwayTeam} MissingSelectionKey={MissingSelectionKey} MissingPrice={MissingPrice} RegistryCandidates={RegistryCandidates} ObservedTeams={ObservedTeams} TeamMatchedCandidates={TeamMatchedCandidates} ActiveCandidates={ActiveCandidates} WindowStart={WindowStart} WindowEnd={WindowEnd}",
                sportKey,
                observedBuild.RawSnapshots.ToString(),
                observations.Count,
                observedBuild.MissingEventId,
                observedBuild.MissingHomeTeam,
                observedBuild.MissingAwayTeam,
                observedBuild.MissingSelectionKey,
                observedBuild.MissingPrice,
                activeSlice.RegistryCandidates.Count(),
                activeSlice.ObservedTeams.Count(),
                activeSlice.TeamMatchedCandidates.Count(),
                activeSlice.ActiveCandidates.Count(),
                activeSlice.WindowStart,
                activeSlice.WindowEnd);

            foreach (var team in activeSlice.ObservedTeams.Take(20))
            {
                _logger.LogInformation("Observed team in active slice. Team={Team}", team);
            }

            foreach (var obs in observations.Take(10))
            {
                _logger.LogInformation(
                    "Projection observation preview. EventId={EventId} Bookmaker={Bookmaker} Home={HomeTeam} Away={AwayTeam} SelectionKey={SelectionKey} Price={Price} CommenceTime={CommenceTime}",
                    obs.EventId,
                    obs.BookmakerKey,
                    obs.HomeTeam,
                    obs.AwayTeam,
                    obs.SelectionKey,
                    obs.Price.ToString(CultureInfo.InvariantCulture),
                    obs.CommenceTime);
            }

            foreach (var candidate in activeSlice.TeamMatchedCandidates.Take(10))
            {
                _logger.LogInformation(
                    "Team-matched candidate preview. ConditionId={ConditionId} ReferencedTeam={ReferencedTeam} Question={Question} GameStartTime={GameStartTime}",
                    candidate.ConditionId,
                    candidate.ReferencedTeam,
                    candidate.Question,
                    candidate.GameStartTime);
            }

            foreach (var candidate in activeSlice.ActiveCandidates.Take(10))
            {
                _logger.LogInformation(
                    "Active candidate preview. ConditionId={ConditionId} ReferencedTeam={ReferencedTeam} Question={Question} GameStartTime={GameStartTime}",
                    candidate.ConditionId,
                    candidate.ReferencedTeam,
                    candidate.Question,
                    candidate.GameStartTime);
            }

            foreach (var obs in observations.Take(10))
            {
                if (!TryResolveObservedTeam(obs, out var observedTeam))
                {
                    _logger.LogInformation(
                        "Projection active-slice analysis. EventId={EventId} SelectionKey={SelectionKey} Result=NO_OBSERVED_TEAM",
                        obs.EventId,
                        obs.SelectionKey);
                    continue;
                }

                var normalizedObservedTeam = NormalizeTeam(observedTeam);

                var teamMatches = activeSlice.TeamMatchedCandidates
                    .Where(x =>
                        string.Equals(
                            NormalizeTeam(x.ReferencedTeam),
                            normalizedObservedTeam,
                            StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                var activeMatches = activeSlice.ActiveCandidates
                    .Where(x =>
                        string.Equals(
                            NormalizeTeam(x.ReferencedTeam),
                            normalizedObservedTeam,
                            StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(
                            NormalizeTeam(x.SideALabel),
                            normalizedObservedTeam,
                            StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(
                            NormalizeTeam(x.SideBLabel),
                            normalizedObservedTeam,
                            StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                _logger.LogInformation(
                    "Projection active-slice analysis. EventId={EventId} ObservedTeam={ObservedTeam} NormalizedObservedTeam={NormalizedObservedTeam} TeamMatches={TeamMatches} ActiveMatches={ActiveMatches}",
                    obs.EventId,
                    observedTeam,
                    normalizedObservedTeam,
                    teamMatches.Length,
                    activeMatches.Length);
            }
        }

        private ActiveSliceDiagnostics BuildActivePolymarketCandidateSlice(
            IReadOnlyCollection<ObservedSoccerSelectionSnapshot> observations)
        {
            var allCandidates = _footballMarketRegistry
                .GetQuoteCandidates()
                .ToArray();

            var totalRegistry = allCandidates.Length;
            var passedYesNoCount = allCandidates.Count(x =>
                string.Equals(x.SemanticType, TeamToWinSemanticType, StringComparison.OrdinalIgnoreCase));
            var passedH2hCount = allCandidates.Count(x =>
                string.Equals(x.SemanticType, TeamVsTeamSemanticType, StringComparison.OrdinalIgnoreCase));
            var missingReferencedTeamCount = allCandidates.Count(x => string.IsNullOrWhiteSpace(x.ReferencedTeam));
            var missingYesTokenCount = allCandidates.Count(x => string.IsNullOrWhiteSpace(x.YesTokenId));
            var missingNoTokenCount = allCandidates.Count(x => string.IsNullOrWhiteSpace(x.NoTokenId));

            _logger.LogInformation(
                "Active slice diagnostics. RegistryTotal={Total} PassedYesNo={YesNo} PassedH2h={H2h} MissingReferencedTeam={MissingRef} MissingYesToken={MissingYes} MissingNoToken={MissingNo}",
                totalRegistry,
                passedYesNoCount,
                passedH2hCount,
                missingReferencedTeamCount,
                missingYesTokenCount,
                missingNoTokenCount);

            var yesNoCandidates = allCandidates
                .Where(x =>
                    string.Equals(
                        x.SemanticType,
                        TeamToWinSemanticType,
                        StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(x.ReferencedTeam) &&
                    !string.IsNullOrWhiteSpace(x.YesTokenId) &&
                    !string.IsNullOrWhiteSpace(x.NoTokenId))
                .ToArray();

            var h2hCandidates = allCandidates
                .Where(x =>
                    string.Equals(
                        x.SemanticType,
                        TeamVsTeamSemanticType,
                        StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(x.SideALabel) &&
                    !string.IsNullOrWhiteSpace(x.SideBLabel) &&
                    !string.IsNullOrWhiteSpace(x.SideATokenId) &&
                    !string.IsNullOrWhiteSpace(x.SideBTokenId))
                .ToArray();

            var registryCandidates = yesNoCandidates.Concat(h2hCandidates).ToArray();

            var observedTeams = observations
                .SelectMany(x =>
                {
                    var teams = new List<string>();

                    if (!string.IsNullOrWhiteSpace(x.HomeTeam))
                        teams.Add(x.HomeTeam);
                    if (!string.IsNullOrWhiteSpace(x.AwayTeam))
                        teams.Add(x.AwayTeam);

                    if (!string.IsNullOrWhiteSpace(x.DirectObservedTeam))
                        teams.Add(x.DirectObservedTeam);

                    return teams;
                })
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(NormalizeTeam)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var teamMatchedCandidates = registryCandidates
             .Where(x => IsTeamMatchedCandidate(x, observedTeams))
             .ToArray();

            return new ActiveSliceDiagnostics
            {
                RegistryCandidates = registryCandidates,
                ObservedTeams = observedTeams,
                TeamMatchedCandidates = teamMatchedCandidates,
                ActiveCandidates = teamMatchedCandidates,
                WindowStart = null,
                WindowEnd = null
            };
        }
        private static bool IsTeamMatchedCandidate(
        FootballQuoteCandidate candidate,
        string[] observedTeams)
        {
            // Futebol: YES/NO — igualdade exata com ReferencedTeam
            if (!string.Equals(
                candidate.SemanticType,
                TeamVsTeamSemanticType,
                StringComparison.OrdinalIgnoreCase))
            {
                return !string.IsNullOrWhiteSpace(candidate.ReferencedTeam) &&
                       observedTeams.Contains(
                           NormalizeTeam(candidate.ReferencedTeam),
                           StringComparer.OrdinalIgnoreCase);
            }

            // H2H (NBA): igualdade exata primeiro
            var normalizedSideA = NormalizeTeam(candidate.SideALabel);
            var normalizedSideB = NormalizeTeam(candidate.SideBLabel);

            if (!string.IsNullOrWhiteSpace(normalizedSideA) &&
                observedTeams.Contains(normalizedSideA, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(normalizedSideB) &&
                observedTeams.Contains(normalizedSideB, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }

            // H2H: fallback por nickname/sufixo
            return observedTeams.Any(observedTeam =>
                TryMatchNicknameH2h(observedTeam, normalizedSideA, normalizedSideB));
        }

        private static bool TryMatchNicknameH2h(
            string normalizedObservedTeam,
            string normalizedSideA,
            string normalizedSideB)
        {
            if (string.IsNullOrWhiteSpace(normalizedObservedTeam))
                return false;

            var tokens = normalizedObservedTeam.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            // Ex.: "los angeles lakers" -> tenta "lakers", depois "angeles lakers"
            for (int i = tokens.Length - 1; i >= 0; i--)
            {
                var suffix = string.Join(" ", tokens.Skip(i));

                if (!string.IsNullOrWhiteSpace(normalizedSideA) &&
                    string.Equals(suffix, normalizedSideA, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (!string.IsNullOrWhiteSpace(normalizedSideB) &&
                    string.Equals(suffix, normalizedSideB, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
        private ObservedBuildResult BuildObservedSoccerSelectionSnapshots(
            IReadOnlyCollection<object> rawSnapshots,
            string sportKey,
            DateTime observedAtUtc)
        {
            var observedAt = observedAtUtc.ToString("O");
            var result = new List<ObservedSoccerSelectionSnapshot>();

            var buildResult = new ObservedBuildResult
            {
                RawSnapshots = rawSnapshots.Count
            };

            foreach (var raw in rawSnapshots)
            {
                var eventId = ReadString(raw, "EventId", "Id", "EventKey") ?? string.Empty;
                var bookmakerKey = ReadString(raw, "BookmakerKey", "Bookmaker", "BookmakerName");
                var homeTeam = ReadString(raw, "HomeTeam", "HomeName", "Home") ?? string.Empty;
                var awayTeam = ReadString(raw, "AwayTeam", "AwayName", "Away") ?? string.Empty;
                var selectionKey = ReadString(raw, "SelectionKey", "Selection", "OutcomeKey") ?? string.Empty;
                var price = ReadDecimal(raw, "OddsDecimal", "Price", "Odds", "DecimalOdds");
                var commenceTime = ReadDateIsoString(raw, "CommenceTime", "StartTime", "EventStartTime");
                var directObservedTeam = ReadString(raw, "DirectObservedTeam", "SideLabel", "Participant");

                if (string.IsNullOrWhiteSpace(eventId))
                {
                    buildResult.MissingEventId++;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(selectionKey))
                {
                    buildResult.MissingSelectionKey++;
                    continue;
                }

                if (price is null)
                {
                    buildResult.MissingPrice++;
                    continue;
                }

                if (string.Equals(selectionKey, "HOME", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(selectionKey, "AWAY", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(homeTeam))
                    {
                        buildResult.MissingHomeTeam++;
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(awayTeam))
                    {
                        buildResult.MissingAwayTeam++;
                        continue;
                    }
                }
                else if (string.Equals(selectionKey, "SIDE_A", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(selectionKey, "SIDE_B", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(directObservedTeam))
                    {
                        buildResult.MissingHomeTeam++;
                        continue;
                    }
                }

                result.Add(new ObservedSoccerSelectionSnapshot
                {
                    EventId = eventId,
                    SportKey = sportKey,
                    BookmakerKey = bookmakerKey,
                    CommenceTime = commenceTime,
                    ObservedAt = observedAt,
                    HomeTeam = homeTeam,
                    AwayTeam = awayTeam,
                    SelectionKey = selectionKey,
                    Price = price.Value,
                    DirectObservedTeam = directObservedTeam
                });
            }

            buildResult.Observations = result
                .GroupBy(
                    x => string.Join(
                        "::",
                        x.EventId,
                        x.BookmakerKey ?? string.Empty,
                        x.SelectionKey,
                        x.Price.ToString(CultureInfo.InvariantCulture),
                        x.ObservedAt),
                    StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .ToArray();

            return buildResult;
        }

        private static bool TryResolveObservedTeam(
            ObservedSoccerSelectionSnapshot observation,
            out string team)
        {
            team = string.Empty;

            if (string.Equals(observation.SelectionKey, "HOME", StringComparison.OrdinalIgnoreCase))
            {
                team = observation.HomeTeam;
                return !string.IsNullOrWhiteSpace(team);
            }

            if (string.Equals(observation.SelectionKey, "AWAY", StringComparison.OrdinalIgnoreCase))
            {
                team = observation.AwayTeam;
                return !string.IsNullOrWhiteSpace(team);
            }

            if (string.Equals(observation.SelectionKey, "SIDE_A", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(observation.SelectionKey, "SIDE_B", StringComparison.OrdinalIgnoreCase))
            {
                team = observation.DirectObservedTeam;
                return !string.IsNullOrWhiteSpace(team);
            }

            return false;
        }

        private static string NormalizeTeam(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var text = RemoveDiacritics(value)
                .ToLowerInvariant()
                .Replace(" futebol clube", " ")
                .Replace(" football club", " ")
                .Replace(" fc", " ")
                .Replace(" sc", " ")
                .Replace(" ec", " ")
                .Replace(" cf", " ")
                .Replace(" ac", " ")
                .Replace(" club", " ")
                .Replace(" de futbol", " ")
                .Replace(" futbol", " ")
                .Replace(" football", " ");

            var sb = new StringBuilder(text.Length);

            foreach (var ch in text)
            {
                sb.Append(char.IsLetterOrDigit(ch) || ch == ' ' ? ch : ' ');
            }

            return string.Join(
                " ",
                sb.ToString()
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries |
                                StringSplitOptions.TrimEntries));
        }

        private static string RemoveDiacritics(string text)
        {
            var normalized = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(normalized.Length);

            foreach (var ch in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) !=
                    UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(ch);
                }
            }

            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        private static string? ReadString(object obj, params string[] fieldNames)
        {
            foreach (var fieldName in fieldNames)
            {
                var property = obj?.GetType()?.GetProperty(
                    fieldName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);

                if (property != null && property.CanRead)
                {
                    var value = property.GetValue(obj) as string;
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }
            }

            return null;
        }

        private static decimal? ReadDecimal(object obj, params string[] fieldNames)
        {
            foreach (var fieldName in fieldNames)
            {
                var property = obj?.GetType()?.GetProperty(
                    fieldName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);

                if (property != null && property.CanRead)
                {
                    try
                    {
                        var value = property.GetValue(obj);
                        if (value is decimal dec)
                            return dec;
                        else if (value is double dbl)
                            return (decimal)dbl;
                        else if (value is int i)
                            return (decimal)i;
                    }
                    catch
                    {
                    }
                }
            }

            return null;
        }

        private static string? ReadDateIsoString(object obj, params string[] fieldNames)
        {
            foreach (var fieldName in fieldNames)
            {
                var property = obj?.GetType()?.GetProperty(
                    fieldName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);

                if (property != null && property.CanRead)
                {
                    var value = property.GetValue(obj);
                    if (value is string str && !string.IsNullOrWhiteSpace(str))
                        return str;
                    else if (value is DateTime dt)
                        return dt.ToString("O");
                }
            }

            return null;
        }
    }

    internal class SportPollState
    {
        public DateTime? LastPolledAtUtc { get; set; }
        public DateTime? NearestEventCommenceTimeUtc { get; set; }
    }

    internal class ObservedBuildResult
    {
        public int RawSnapshots { get; set; }
        public int MissingEventId { get; set; }
        public int MissingHomeTeam { get; set; }
        public int MissingAwayTeam { get; set; }
        public int MissingSelectionKey { get; set; }
        public int MissingPrice { get; set; }
        public ObservedSoccerSelectionSnapshot[] Observations { get; set; } = Array.Empty<ObservedSoccerSelectionSnapshot>();
    }

    internal class ActiveSliceDiagnostics
    {
        public FootballQuoteCandidate[] RegistryCandidates { get; set; } = Array.Empty<FootballQuoteCandidate>();
        public string[] ObservedTeams { get; set; } = Array.Empty<string>();
        public FootballQuoteCandidate[] TeamMatchedCandidates { get; set; } = Array.Empty<FootballQuoteCandidate>();
        public FootballQuoteCandidate[] ActiveCandidates { get; set; } = Array.Empty<FootballQuoteCandidate>();
        public string? WindowStart { get; set; }
        public string? WindowEnd { get; set; }
    }
}