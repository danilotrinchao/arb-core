using Arb.Core.Application.Abstractions.Execution;

namespace Arb.Core.Application.Abstractions.Execution
{
    public interface IExecutionDispatchService
    {
        Task<ExecutionCommandResponseDto> DispatchBuyAsync(
            ExecutionCommandDto command,
            CancellationToken ct);

        Task<ExecutionCommandResponseDto> DispatchSellAsync(
            ExecutionCommandDto command,
            CancellationToken ct);
    }
}