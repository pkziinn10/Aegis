namespace Aegis.Infrastructure.Persistence;

/// <summary>
/// Retained only as a source-compatibility marker for consumers awaiting migration
/// to <see cref="Aegis.Application.Abstractions.IUnitOfWork"/>. Transactions are
/// exclusively owned by <c>EfUnitOfWork</c> and this type is not registered in DI.
/// </summary>
[Obsolete("Use IUnitOfWork.ExecuteInTransactionAsync; TransactionRunner is no longer available.")]
public sealed class TransactionRunner
{
    public TransactionRunner(AegisDbContext db) => ArgumentNullException.ThrowIfNull(db);

    public Task<T> ExecuteOutcomeAsync<T>(Func<CancellationToken, Task<(T Result, bool Commit)>> operation, CancellationToken ct) =>
        throw new NotSupportedException("Use IUnitOfWork.ExecuteInTransactionAsync.");

    public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct) =>
        throw new NotSupportedException("Use IUnitOfWork.ExecuteInTransactionAsync.");
}
