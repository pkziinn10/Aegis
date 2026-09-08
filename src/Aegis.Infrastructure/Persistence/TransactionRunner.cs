using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Aegis.Infrastructure.Persistence;

public sealed class TransactionRunner(AegisDbContext db)
{
    public Task<T> ExecuteOutcomeAsync<T>(Func<CancellationToken, Task<(T Result, bool Commit)>> operation, CancellationToken ct) => ExecuteOutcomeCoreAsync(operation, ct);
    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null) return await operation(ct);
        for (var attempt = 0; ; attempt++)
        {
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
            try { var result = await operation(ct); await tx.CommitAsync(ct); return result; }
            catch (Exception ex) when ((IsRetryable(ex)) && attempt < 2) { db.ChangeTracker.Clear(); }
        }
    }
    private static bool IsSerialization(Exception ex) => ex switch
    {
        PostgresException { SqlState: PostgresErrorCodes.SerializationFailure } => true,
        _ when ex.InnerException is not null => IsSerialization(ex.InnerException),
        _ => false
    };
    private async Task<T> ExecuteOutcomeCoreAsync<T>(Func<CancellationToken, Task<(T Result, bool Commit)>> operation, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null) { var nested = await operation(ct); return nested.Result; }
        for (var attempt = 0; ; attempt++)
        {
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
            try
            {
                var outcome = await operation(ct);
                if (outcome.Commit) await tx.CommitAsync(ct); else await tx.RollbackAsync(ct);
                return outcome.Result;
            }
            catch (Exception ex) when (IsRetryable(ex) && attempt < 2) { db.ChangeTracker.Clear(); }
        }
    }
    private static bool IsRetryable(Exception ex) => ex is DbUpdateConcurrencyException || IsSerialization(ex) || (ex.InnerException is not null && IsRetryable(ex.InnerException));
}
