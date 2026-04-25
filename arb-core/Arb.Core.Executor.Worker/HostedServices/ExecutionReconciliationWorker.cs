using Arb.Core.Application.Abstractions.Execution;
using Arb.Core.Application.Abstractions.Messaging;
using Arb.Core.Application.Abstractions.Persistence;
using Arb.Core.Contracts.Events;
using Arb.Core.Executor.Worker.Options;
using Arb.Core.Infrastructure.Redis;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text.Json;

namespace Arb.Core.Executor.Worker
{
    public class ExecutionReconciliationWorker : BackgroundService
    {
        private readonly ILogger<ExecutionReconciliationWorker> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IExecutionGateway _executionGateway;
        private readonly IStreamPublisher _publisher;
        private readonly StreamsOptions _streams;
        private readonly RiskOptions _risk;

        public ExecutionReconciliationWorker(
            ILogger<ExecutionReconciliationWorker> logger,
            IServiceScopeFactory scopeFactory,
            IExecutionGateway executionGateway,
            IStreamPublisher publisher,
            IOptions<StreamsOptions> streamsOptions,
            IOptions<RiskOptions> riskOptions)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
            _executionGateway = executionGateway;
            _publisher = publisher;
            _streams = streamsOptions.Value;
            _risk = riskOptions.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "ExecutionReconciliationWorker started. PollInterval=10s");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ReconcileBatchAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Execution reconciliation cycle failed");
                }

                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }

        private async Task ReconcileBatchAsync(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();

            var requestRepo = scope.ServiceProvider.GetRequiredService<IExecutionRequestRepository>();
            var fillRepo = scope.ServiceProvider.GetRequiredService<IExecutionFillRepository>();
            var positionRepo = scope.ServiceProvider.GetRequiredService<IPositionRepository>();
            var portfolioRepo = scope.ServiceProvider.GetRequiredService<IPortfolioRepository>();
            var reportRepo = scope.ServiceProvider.GetRequiredService<IExecutionReportRepository>();

            var pending = await requestRepo.ListPendingReconciliationAsync(50, ct);
            if (pending.Count == 0)
                return;

            foreach (var request in pending)
            {
                if (string.IsNullOrWhiteSpace(request.ExternalOrderId))
                    continue;

                try
                {
                    var order = await _executionGateway.GetOrderAsync(request.ExternalOrderId, ct);
                    var fills = await _executionGateway.GetFillsAsync(request.ExternalOrderId, ct);

                    if (fills.Success)
                    {
                        foreach (var fill in fills.Fills)
                        {
                            var exists = await fillRepo.ExistsByExternalFillIdAsync(fill.ExternalFillId, ct);
                            if (exists)
                                continue;

                            await fillRepo.InsertAsync(
                                new ExecutionFillRecord(
                                    Id: Guid.NewGuid(),
                                    ExecutionRequestId: request.Id,
                                    ExternalOrderId: request.ExternalOrderId,
                                    ExternalFillId: fill.ExternalFillId,
                                    TokenId: request.TokenId,
                                    Side: fill.Side,
                                    FillPrice: fill.Price,
                                    FillSizeUsd: fill.SizeUsd,
                                    ExecutedAt: fill.ExecutedAt,
                                    RawPayload: JsonSerializer.Serialize(fill)),
                                ct);
                        }
                    }

                    var totalFilledUsd = await fillRepo.SumFilledSizeUsdAsync(request.Id, ct);

                    if (order.Success)
                    {
                        switch (order.Status)
                        {
                            case "PARTIALLY_FILLED":
                                await requestRepo.MarkPartiallyFilledAsync(
                                    request.Id,
                                    order.RawJson,
                                    DateTime.UtcNow,
                                    ct);
                                break;

                            case "FILLED":
                                await requestRepo.MarkFilledAsync(
                                    request.Id,
                                    order.RawJson,
                                    DateTime.UtcNow,
                                    ct);

                                await MaterializeOpenedPositionAsync(
                                    request,
                                    totalFilledUsd,
                                    requestRepo,
                                    fillRepo,
                                    positionRepo,
                                    portfolioRepo,
                                    reportRepo,
                                    ct);
                                break;

                            case "CANCELLED":
                                await requestRepo.MarkCancelledAsync(
                                    request.Id,
                                    order.RawJson,
                                    DateTime.UtcNow,
                                    ct);
                                break;

                            case "EXPIRED":
                                await requestRepo.MarkExpiredAsync(
                                    request.Id,
                                    order.RawJson,
                                    DateTime.UtcNow,
                                    ct);
                                break;
                        }
                    }

                    await requestRepo.MarkReconciledAsync(
                        request.Id,
                        DateTime.UtcNow,
                        ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Failed reconciling execution request. requestId={RequestId} externalOrderId={ExternalOrderId}",
                        request.Id,
                        request.ExternalOrderId);
                }
            }
        }

        private async Task MaterializeOpenedPositionAsync(
            ExecutionRequestRecord request,
            double totalFilledUsd,
            IExecutionRequestRepository requestRepo,
            IExecutionFillRepository fillRepo,
            IPositionRepository positionRepo,
            IPortfolioRepository portfolioRepo,
            IExecutionReportRepository reportRepo,
            CancellationToken ct)
        {
            if (request.Action != "BUY")
                return;

            if (!string.IsNullOrWhiteSpace(request.MaterializedPositionId))
                return;

            var portfolio = await portfolioRepo.GetAsync(ct);
            if (portfolio is null)
                return;

            var fillPrice = request.LimitPrice;
            var metadata = ParseMetadata(request.RawRequest);

            var commenceTime =
                ParseDateTime(GetString(metadata, "commenceTime")) ??
                DateTime.UtcNow.AddHours(6);

            var targetProbability =
                ParseNullableDouble(GetString(metadata, "targetProbability"));

            var positionId = await positionRepo.CreateOpenAsync(
                new PositionOpen(
                    IntentId: request.IntentId?.ToString("D") ?? string.Empty,
                    SportKey: GetString(metadata, "sportKey") ?? "unknown",
                    EventKey: GetString(metadata, "eventKey") ?? string.Empty,
                    HomeTeam: string.Empty,
                    AwayTeam: string.Empty,
                    CommenceTime: commenceTime,
                    MarketType: "TEAM_TO_WIN_YES_NO",
                    SelectionKey: GetString(metadata, "selectionKey") ?? string.Empty,
                    Stake: totalFilledUsd,
                    EntryPrice: fillPrice,
                    CreatedAt: DateTime.UtcNow,
                    TargetSide: GetString(metadata, "targetSide"),
                    ObservedTeam: GetString(metadata, "observedTeam"),
                    PolymarketConditionId: request.MarketConditionId,
                    PolymarketEntryPrice: fillPrice,
                    TargetProbability: targetProbability,
                    TargetTokenId: request.TokenId),
                ct);

            if (positionId == Guid.Empty)
                return;

            var newBalance = portfolio.CurrentBalance - totalFilledUsd;

            await portfolioRepo.UpdateBalanceAsync(
                newBalance,
                DateTime.UtcNow,
                ct);

            await requestRepo.MarkMaterializedAsync(
                request.Id,
                positionId.ToString("D"),
                DateTime.UtcNow,
                DateTime.UtcNow,
                ct);

            var opened = new ExecutionReportV1(
                SchemaVersion: "1.0.0",
                ReportId: Guid.NewGuid().ToString("N"),
                IntentId: request.IntentId?.ToString("D") ?? string.Empty,
                CorrelationId: request.CorrelationId,
                Ts: DateTime.UtcNow,
                Status: "OPENED",
                FilledPrice: fillPrice,
                FilledUsd: totalFilledUsd,
                TxHash: request.ExternalOrderId,
                Error: null);

            await PersistAndPublishReportAsync(opened, reportRepo, ct);

            _logger.LogInformation(
                "Position materialized from real fill. requestId={RequestId} positionId={PositionId} externalOrderId={ExternalOrderId} filledUsd={FilledUsd}",
                request.Id,
                positionId,
                request.ExternalOrderId,
                totalFilledUsd);
        }

        private async Task PersistAndPublishReportAsync(
            ExecutionReportV1 report,
            IExecutionReportRepository repo,
            CancellationToken ct)
        {
            await repo.InsertAsync(report, ct);

            var payload = JsonSerializer.Serialize(report);

            var fields = new Dictionary<string, string>
            {
                ["schema"] = "ExecutionReportV1",
                ["schemaVersion"] = report.SchemaVersion,
                ["reportId"] = report.ReportId,
                ["intentId"] = report.IntentId,
                ["correlationId"] = report.CorrelationId,
                ["ts"] = report.Ts.ToString("O"),
                ["status"] = report.Status,
                ["payload"] = payload
            };

            await _publisher.PublishAsync(_streams.ExecutionReports, fields, ct);
        }

        private static Dictionary<string, object>? ParseMetadata(string rawRequest)
        {
            try
            {
                using var doc = JsonDocument.Parse(rawRequest);

                if (!doc.RootElement.TryGetProperty("Metadata", out var metadataElement) ||
                    metadataElement.ValueKind != JsonValueKind.Object)
                    return null;

                var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

                foreach (var prop in metadataElement.EnumerateObject())
                {
                    result[prop.Name] = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => prop.Value.GetString() ?? string.Empty,
                        JsonValueKind.Number => prop.Value.TryGetDouble(out var d) ? d : 0d,
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        _ => prop.Value.ToString()
                    };
                }

                return result;
            }
            catch
            {
                return null;
            }
        }

        private static string? GetString(Dictionary<string, object>? metadata, string key)
        {
            if (metadata is null)
                return null;

            return metadata.TryGetValue(key, out var value)
                ? value?.ToString()
                : null;
        }

        private static DateTime? ParseDateTime(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed)
                ? parsed
                : null;
        }

        private static double? ParseNullableDouble(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return double.TryParse(
                value,
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out var parsed)
                ? parsed
                : null;
        }
    }
}