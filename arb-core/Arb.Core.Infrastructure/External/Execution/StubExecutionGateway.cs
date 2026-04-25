using Arb.Core.Application.Abstractions.Execution;

namespace Arb.Core.Infrastructure.External.Execution
{
    public class StubExecutionGateway : IExecutionGateway
    {
        public Task<ExecutionCommandResponseDto> PlaceBuyAsync(
            ExecutionCommandDto command,
            CancellationToken ct)
        {
            return Task.FromResult(new ExecutionCommandResponseDto
            {
                Success = false,
                RequestId = command.RequestId,
                Status = "NOT_IMPLEMENTED",
                ErrorCode = "EXECUTION_GATEWAY_NOT_IMPLEMENTED",
                ErrorMessage = "Real execution adapter has not been created yet."
            });
        }

        public Task<ExecutionCommandResponseDto> PlaceSellAsync(
            ExecutionCommandDto command,
            CancellationToken ct)
        {
            return Task.FromResult(new ExecutionCommandResponseDto
            {
                Success = false,
                RequestId = command.RequestId,
                Status = "NOT_IMPLEMENTED",
                ErrorCode = "EXECUTION_GATEWAY_NOT_IMPLEMENTED",
                ErrorMessage = "Real execution adapter has not been created yet."
            });
        }

        public Task<ExecutionCommandResponseDto> CancelAsync(
            CancelExecutionCommandDto command,
            CancellationToken ct)
        {
            return Task.FromResult(new ExecutionCommandResponseDto
            {
                Success = false,
                RequestId = command.RequestId,
                Status = "NOT_IMPLEMENTED",
                ErrorCode = "EXECUTION_GATEWAY_NOT_IMPLEMENTED",
                ErrorMessage = "Real execution adapter has not been created yet."
            });
        }

        public Task<OrderStatusDto> GetOrderAsync(
            string externalOrderId,
            CancellationToken ct)
        {
            return Task.FromResult(new OrderStatusDto
            {
                Success = false,
                ExternalOrderId = externalOrderId,
                Status = "NOT_IMPLEMENTED"
            });
        }

        public Task<OrderFillsDto> GetFillsAsync(
            string externalOrderId,
            CancellationToken ct)
        {
            return Task.FromResult(new OrderFillsDto
            {
                Success = false,
                ExternalOrderId = externalOrderId
            });
        }

        public Task<WalletBalanceDto> GetBalanceAsync(CancellationToken ct)
        {
            return Task.FromResult(new WalletBalanceDto
            {
                Success = false,
                WalletAddress = string.Empty,
                AvailableUsd = 0,
                TotalUsd = 0
            });
        }
    }
}