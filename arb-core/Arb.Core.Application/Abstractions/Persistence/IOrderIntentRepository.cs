using Arb.Core.Contracts.Events;

namespace Arb.Core.Application.Abstractions.Persistence
{
    public interface IOrderIntentRepository
    {
        Task InsertAsync(OrderIntentV1 intent, CancellationToken ct);
    }
}
