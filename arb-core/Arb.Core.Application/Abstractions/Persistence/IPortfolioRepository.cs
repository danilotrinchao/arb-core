namespace Arb.Core.Application.Abstractions.Persistence
{
    public interface IPortfolioRepository
    {
        Task<PortfolioState?> GetAsync(CancellationToken ct);
        Task EnsureInitializedAsync(double initialBalance, CancellationToken ct);
        Task UpdateBalanceAsync(double newBalance, DateTime updatedAt, CancellationToken ct);
    }

    public sealed record PortfolioState(
        Guid Id,
        double InitialBalance,
        double CurrentBalance,
        DateTime UpdatedAt
    );
}
