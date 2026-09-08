using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable
namespace Aegis.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AegisDbContext))]
[Migration("202609080001_Initial")]
partial class Initial
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.11");
        modelBuilder.Entity("Aegis.Infrastructure.Persistence.UserRow", b => { b.Property<Guid>("Id"); b.Property<string>("Email").HasMaxLength(254).IsRequired(); b.Property<bool>("IsActive"); b.Property<string>("PasswordHash").IsRequired(); b.Property<int>("Role"); b.Property<long>("Version").IsConcurrencyToken(); b.HasKey("Id"); b.HasIndex("Email").IsUnique(); b.ToTable("users"); });
        modelBuilder.Entity("Aegis.Infrastructure.Persistence.SessionRow", b => { b.Property<Guid>("Id"); b.Property<DateTimeOffset>("CreatedAt"); b.Property<DateTimeOffset>("ExpiresAt"); b.Property<int?>("RevocationReason"); b.Property<DateTimeOffset?>("RevokedAt"); b.Property<Guid>("UserId"); b.Property<long>("Version").IsConcurrencyToken(); b.HasKey("Id"); b.HasIndex("UserId", "RevokedAt"); b.ToTable("sessions"); });
        modelBuilder.Entity("Aegis.Infrastructure.Persistence.RefreshTokenRow", b => { b.Property<Guid>("Id"); b.Property<DateTimeOffset>("CreatedAt"); b.Property<DateTimeOffset>("ExpiresAt"); b.Property<string>("Hash").HasMaxLength(64).IsRequired(); b.Property<DateTimeOffset?>("RevokedAt"); b.Property<Guid>("SessionId"); b.HasKey("Id"); b.HasIndex("Hash").IsUnique(); b.HasIndex("SessionId").HasDatabaseName("IX_refresh_tokens_one_active_per_session").HasFilter("\"RevokedAt\" IS NULL").IsUnique(); b.ToTable("refresh_tokens"); });
        modelBuilder.Entity("Aegis.Infrastructure.Persistence.AuditEventRow", b => { b.Property<Guid>("Id"); b.Property<string>("Action").HasMaxLength(100).IsRequired(); b.Property<DateTimeOffset>("CreatedAt"); b.Property<string>("MetadataJson").HasMaxLength(4000); b.Property<Guid?>("UserId"); b.HasKey("Id"); b.HasIndex("CreatedAt"); b.ToTable("audit_events"); });
        modelBuilder.Entity("Aegis.Infrastructure.Persistence.RefreshTokenRow", b => b.HasOne("Aegis.Infrastructure.Persistence.SessionRow").WithMany("RefreshTokens").HasForeignKey("SessionId").OnDelete(DeleteBehavior.Cascade).IsRequired());
        modelBuilder.Entity("Aegis.Infrastructure.Persistence.SessionRow", b => b.HasOne("Aegis.Infrastructure.Persistence.UserRow").WithMany().HasForeignKey("UserId").OnDelete(DeleteBehavior.Cascade).IsRequired());
        modelBuilder.Entity("Aegis.Infrastructure.Persistence.SessionRow", b => b.Navigation("RefreshTokens"));
    }
}
