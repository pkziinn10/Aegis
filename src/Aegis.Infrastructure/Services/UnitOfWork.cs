using Aegis.Application.Abstractions;
using Aegis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Aegis.Infrastructure.Services;

public sealed class EfUnitOfWork(AegisDbContext db) : IUnitOfWork
{
    public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<TransactionOutcome<T>>> operation, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (db.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("Nested transactions are not supported.");

        for (var attempt = 0; ; attempt++)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
            try
            {
                var outcome = await operation(ct);
                if (outcome.Decision == TransactionDecision.Commit)
                {
                    await db.SaveChangesAsync(ct);
                    await transaction.CommitAsync(ct);
                }
                else
                {
                    await transaction.RollbackAsync(ct);
                    db.ChangeTracker.Clear();
                }

                return outcome.Result;
            }
            catch (Exception ex) when (IsRetryable(ex) && attempt < 2)
            {
                await RollbackAndClearAsync(transaction, ct);
            }
            catch
            {
                await RollbackAndClearAsync(transaction, ct);
                throw;
            }
        }
    }

    private async Task RollbackAndClearAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction, CancellationToken ct)
    {
        try { await transaction.RollbackAsync(ct); }
        finally { db.ChangeTracker.Clear(); }
    }

    private static bool IsRetryable(Exception ex) => IsSerialization(ex) || IsDeadlock(ex) ||
        (ex.InnerException is not null && IsRetryable(ex.InnerException));

    private static bool IsSerialization(Exception ex) => ex switch
    {
        PostgresException { SqlState: PostgresErrorCodes.SerializationFailure } => true,
        _ when ex.InnerException is not null => IsSerialization(ex.InnerException),
        _ => false
    };

    private static bool IsDeadlock(Exception ex) => ex switch
    {
        PostgresException { SqlState: PostgresErrorCodes.DeadlockDetected } => true,
        _ when ex.InnerException is not null => IsDeadlock(ex.InnerException),
        _ => false
    };
}
