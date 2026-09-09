using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Aegis.Infrastructure.Persistence.Migrations;

public partial class Initial : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable("users", table => new
        {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
            PasswordHash = table.Column<string>(type: "text", nullable: false),
            Role = table.Column<int>(type: "integer", nullable: false),
            IsActive = table.Column<bool>(type: "boolean", nullable: false),
            Version = table.Column<long>(type: "bigint", nullable: false)
        }, constraints: table => table.PrimaryKey("PK_users", x => x.Id));
        m.CreateTable("sessions", table => new
        {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            UserId = table.Column<Guid>(type: "uuid", nullable: false),
            CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
            RevocationReason = table.Column<int>(type: "integer", nullable: true),
            Version = table.Column<long>(type: "bigint", nullable: false)
        }, constraints: table => { table.PrimaryKey("PK_sessions", x => x.Id); table.ForeignKey("FK_sessions_users", x => x.UserId, "users", "Id", onDelete: ReferentialAction.Cascade); });
        m.CreateTable("audit_events", table => new
        {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            UserId = table.Column<Guid>(type: "uuid", nullable: true),
            Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
            MetadataJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
            CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
        }, constraints: table => table.PrimaryKey("PK_audit_events", x => x.Id));
        m.CreateTable("refresh_tokens", table => new
        {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            SessionId = table.Column<Guid>(type: "uuid", nullable: false),
            Hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
            CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
        }, constraints: table => { table.PrimaryKey("PK_refresh_tokens", x => x.Id); table.ForeignKey("FK_refresh_tokens_sessions", x => x.SessionId, "sessions", "Id", onDelete: ReferentialAction.Cascade); });
        m.CreateIndex("IX_audit_events_CreatedAt", "audit_events", "CreatedAt");
        m.CreateIndex("IX_refresh_tokens_Hash", "refresh_tokens", "Hash", unique: true);
        m.CreateIndex("IX_refresh_tokens_one_active_per_session", "refresh_tokens", "SessionId", unique: true, filter: "\"RevokedAt\" IS NULL");
        m.CreateIndex("IX_sessions_UserId_RevokedAt", "sessions", new[] { "UserId", "RevokedAt" });
        m.CreateIndex("IX_users_Email", "users", "Email", unique: true);
    }

    protected override void Down(MigrationBuilder m)
    {
        m.DropTable("refresh_tokens");
        m.DropTable("audit_events");
        m.DropTable("sessions");
        m.DropTable("users");
    }
}
