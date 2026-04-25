namespace Arb.Core.Application.Abstractions.Persistence
{
    public interface IExecutionFillRepository
    {
        Task InsertAsync(ExecutionFillRecord fill, CancellationToken ct);
        Task<bool> ExistsByExternalFillIdAsync(string externalFillId, CancellationToken ct);
        Task<double> SumFilledSizeUsdAsync(Guid executionRequestId, CancellationToken ct);
    }

public record ExecutionFillRecord(
        Guid Id,
        Guid ExecutionRequestId,
        string ExternalOrderId,
        string ExternalFillId,
        string TokenId,
        string Side,
        double FillPrice,
        double FillSizeUsd,
        DateTime ExecutedAt,
        string RawPayload
    );
}