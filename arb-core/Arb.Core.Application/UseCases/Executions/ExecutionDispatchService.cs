using System.Text.Json;
using Arb.Core.Application.Abstractions.Execution;
using Arb.Core.Application.Abstractions.Persistence;

namespace Arb.Core.Application.Services
{
    public class ExecutionDispatchService : IExecutionDispatchService
    {
        private readonly IExecutionGateway _executionGateway;
        private readonly IExecutionRequestRepository _executionRequestRepository;

        public ExecutionDispatchService(
            IExecutionGateway executionGateway,
            IExecutionRequestRepository executionRequestRepository)
        {
            _executionGateway = executionGateway;
            _executionRequestRepository = executionRequestRepository;
        }

        public async Task<ExecutionCommandResponseDto> DispatchBuyAsync(
            ExecutionCommandDto command,
            CancellationToken ct)
        {
            return await DispatchAsync(command, isBuy: true, ct);
        }

        public async Task<ExecutionCommandResponseDto> DispatchSellAsync(
            ExecutionCommandDto command,
            CancellationToken ct)
        {
            return await DispatchAsync(command, isBuy: false, ct);
        }

        private async Task<ExecutionCommandResponseDto> DispatchAsync(
            ExecutionCommandDto command,
            bool isBuy,
            CancellationToken ct)
        {
            var utcNow = DateTime.UtcNow;

            var request = new ExecutionRequestRecord
            {
                Id = command.RequestId,
                IntentId = command.IntentId,
                PositionId = command.PositionId?.ToString(),
                Action = isBuy ? "BUY" : "SELL",
                TokenId = command.TokenId,
                MarketConditionId = command.MarketConditionId,
                Side = command.Side,
                LimitPrice = command.Price,
                RequestedSizeUsd = command.SizeUsd,
                Status = "PENDING_DISPATCH",
                CorrelationId = command.CorrelationId,
                ExternalOrderId = null,
                ErrorCode = null,
                ErrorMessage = null,
                CreatedAt = utcNow,
                SentAt = null,
                UpdatedAt = utcNow,
                RawRequest = JsonSerializer.Serialize(command),
                RawResponse = null,
                MaterializedPositionId = null,
                MaterializedAt = null,
                LastReconciliationAt = null
            };
            await _executionRequestRepository.InsertAsync(request, ct);

            await _executionRequestRepository.MarkDispatchedAsync(
                command.RequestId,
                DateTime.UtcNow,
                ct);

            ExecutionCommandResponseDto response;

            try
            {
                response = isBuy
                    ? await _executionGateway.PlaceBuyAsync(command, ct)
                    : await _executionGateway.PlaceSellAsync(command, ct);
            }
            catch (Exception ex)
            {
                var rawError = JsonSerializer.Serialize(new
                {
                    exception = ex.GetType().Name,
                    message = ex.Message
                });

                await _executionRequestRepository.MarkFailedAsync(
                    command.RequestId,
                    errorCode: "DISPATCH_EXCEPTION",
                    errorMessage: ex.Message,
                    rawResponse: rawError,
                    updatedAt: DateTime.UtcNow,
                    ct: ct);

                return new ExecutionCommandResponseDto
                {
                    Success = false,
                    RequestId = command.RequestId,
                    Status = "FAILED",
                    ErrorCode = "DISPATCH_EXCEPTION",
                    ErrorMessage = ex.Message,
                    RawJson = rawError
                };
            }

            if (response.Success && !string.IsNullOrWhiteSpace(response.ExternalOrderId))
            {
                await _executionRequestRepository.MarkAcceptedAsync(
                    command.RequestId,
                    response.ExternalOrderId,
                    response.RawJson,
                    DateTime.UtcNow,
                    ct);
            }
            else if (response.Status == "REJECTED")
            {
                await _executionRequestRepository.MarkRejectedAsync(
                    command.RequestId,
                    response.ErrorCode,
                    response.ErrorMessage,
                    response.RawJson,
                    DateTime.UtcNow,
                    ct);
            }
            else
            {
                await _executionRequestRepository.MarkFailedAsync(
                    command.RequestId,
                    response.ErrorCode,
                    response.ErrorMessage,
                    response.RawJson,
                    DateTime.UtcNow,
                    ct);
            }

            return response;
        }
    }
}