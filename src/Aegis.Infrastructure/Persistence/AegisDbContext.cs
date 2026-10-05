using Microsoft.EntityFrameworkCore;

namespace Aegis.Infrastructure.Persistence;

public sealed class AegisDbContext(DbContextOptions<AegisDbContext> options) : DbContext(options)
{
    public DbSet<UserRow> Users => Set<UserRow>();
    public DbSet<SessionRow> Sessions => Set<SessionRow>();
    public DbSet<RefreshTokenRow> RefreshTokens => Set<RefreshTokenRow>();
    public DbSet<AuditEventRow> AuditEvents => Set<AuditEventRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserRow>(b => { b.ToTable("users"); b.HasKey(x => x.Id); b.Property(x => x.Email).HasMaxLength(254).IsRequired(); b.HasIndex(x => x.Email).IsUnique(); b.Property(x => x.Version).IsConcurrencyToken(); });
        modelBuilder.Entity<SessionRow>(b => { b.ToTable("sessions", t => { t.HasCheckConstraint("CK_sessions_revocation_pair", "(\"RevokedAt\" IS NULL AND \"RevocationReason\" IS NULL) OR (\"RevokedAt\" IS NOT NULL AND \"RevocationReason\" IS NOT NULL)"); t.HasCheckConstraint("CK_sessions_revocation_reason_range", "\"RevocationReason\" IS NULL OR \"RevocationReason\" BETWEEN 0 AND 3"); }); b.HasKey(x => x.Id); b.Property(x => x.Version).IsConcurrencyToken(); b.HasOne<UserRow>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade); b.HasMany(x => x.RefreshTokens).WithOne().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade); b.HasIndex(x => new { x.UserId, x.RevokedAt }); });
        modelBuilder.Entity<RefreshTokenRow>(b => { b.ToTable("refresh_tokens"); b.HasKey(x => x.Id); b.Property(x => x.Hash).HasMaxLength(64).IsRequired(); b.HasIndex(x => x.Hash).IsUnique(); b.HasIndex(x => x.SessionId).HasDatabaseName("IX_refresh_tokens_session_id"); b.HasIndex(x => x.SessionId).HasDatabaseName("IX_refresh_tokens_one_active_per_session").HasFilter("\"RevokedAt\" IS NULL").IsUnique(); });
        modelBuilder.Entity<AuditEventRow>(b => { b.ToTable("audit_events"); b.HasKey(x => x.Id); b.Property(x => x.Action).HasMaxLength(100).IsRequired(); b.Property(x => x.MetadataJson).HasMaxLength(4000); b.HasIndex(x => x.CreatedAt); });
    }
}
