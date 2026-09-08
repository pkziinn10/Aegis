using Aegis.Application.Abstractions;
using Aegis.Infrastructure.Persistence;

namespace Aegis.Infrastructure.Services;

public sealed class EfUnitOfWork(AegisDbContext db, TransactionRunner transactions) : IUnitOfWork
{
    public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<TransactionOutcome<T>>> operation, CancellationToken ct = default)
    {
        return await transactions.ExecuteOutcomeAsync(async token => { var outcome = await operation(token); if (outcome.Decision == TransactionDecision.Commit) await db.SaveChangesAsync(token); return (outcome.Result, outcome.Decision == TransactionDecision.Commit); }, ct);
    }
}
