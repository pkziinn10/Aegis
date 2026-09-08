using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Aegis.Infrastructure.Persistence.Migrations;

public partial class Initial : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.Sql("CREATE TABLE users (\"Id\" uuid NOT NULL PRIMARY KEY, \"Email\" varchar(254) NOT NULL UNIQUE, \"PasswordHash\" text NOT NULL, \"Role\" integer NOT NULL, \"IsActive\" boolean NOT NULL, \"Version\" bigint NOT NULL);");
        m.Sql("CREATE TABLE sessions (\"Id\" uuid NOT NULL PRIMARY KEY, \"UserId\" uuid NOT NULL REFERENCES users(\"Id\") ON DELETE CASCADE, \"CreatedAt\" timestamptz NOT NULL, \"ExpiresAt\" timestamptz NOT NULL, \"RevokedAt\" timestamptz NULL, \"RevocationReason\" integer NULL, \"Version\" bigint NOT NULL);");
        m.Sql("CREATE TABLE audit_events (\"Id\" uuid NOT NULL PRIMARY KEY, \"UserId\" uuid NULL, \"Action\" varchar(100) NOT NULL, \"MetadataJson\" varchar(4000) NULL, \"CreatedAt\" timestamptz NOT NULL);");
        m.Sql("CREATE TABLE refresh_tokens (\"Id\" uuid NOT NULL PRIMARY KEY, \"SessionId\" uuid NOT NULL REFERENCES sessions(\"Id\") ON DELETE CASCADE, \"Hash\" varchar(64) NOT NULL UNIQUE, \"CreatedAt\" timestamptz NOT NULL, \"ExpiresAt\" timestamptz NOT NULL, \"RevokedAt\" timestamptz NULL);");
        m.Sql("CREATE INDEX \"IX_audit_events_CreatedAt\" ON audit_events (\"CreatedAt\"); CREATE INDEX \"IX_sessions_UserId_RevokedAt\" ON sessions (\"UserId\", \"RevokedAt\"); CREATE UNIQUE INDEX \"IX_refresh_tokens_one_active_per_session\" ON refresh_tokens (\"SessionId\") WHERE \"RevokedAt\" IS NULL;");
    }
    protected override void Down(MigrationBuilder m) { m.Sql("DROP TABLE IF EXISTS refresh_tokens, audit_events, sessions, users CASCADE;"); }
}
