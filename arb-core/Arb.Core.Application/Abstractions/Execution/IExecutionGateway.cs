namespace Arb.Core.Application.Abstractions.Execution
{
    public interface IExecutionGateway
    {
        Task<ExecutionCommandResponseDto> PlaceBuyAsync(
            ExecutionCommandDto command,
            CancellationToken ct);

        Task<ExecutionCommandResponseDto> PlaceSellAsync(
            ExecutionCommandDto command,
            CancellationToken ct);

        Task<ExecutionCommandResponseDto> CancelAsync(
            CancelExecutionCommandDto command,
            CancellationToken ct);

        Task<OrderStatusDto> GetOrderAsync(
            string externalOrderId,
            CancellationToken ct);

        Task<OrderFillsDto> GetFillsAsync(
            string externalOrderId,
            CancellationToken ct);

        Task<WalletBalanceDto> GetBalanceAsync(
            CancellationToken ct);
    }
}