using Arb.Core.Contracts.Events;

namespace Arb.Core.Application.Abstractions.Persistence
{
    public interface IExecutionReportRepository
    {
        Task InsertAsync(ExecutionReportV1 report, CancellationToken ct);
    }
}
