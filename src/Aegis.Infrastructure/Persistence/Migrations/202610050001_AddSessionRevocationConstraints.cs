using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aegis.Infrastructure.Persistence.Migrations;

public partial class AddSessionRevocationConstraints : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.AddCheckConstraint(
            name: "CK_sessions_revocation_pair",
            table: "sessions",
            sql: "(\"RevokedAt\" IS NULL AND \"RevocationReason\" IS NULL) OR (\"RevokedAt\" IS NOT NULL AND \"RevocationReason\" IS NOT NULL)");
        m.AddCheckConstraint(
            name: "CK_sessions_revocation_reason_range",
            table: "sessions",
            sql: "\"RevocationReason\" IS NULL OR \"RevocationReason\" BETWEEN 0 AND 3");

        m.Sql("""
            CREATE FUNCTION aegis_validate_session_revocation_consistency()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $function$
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM sessions AS s
                    INNER JOIN refresh_tokens AS r ON r."SessionId" = s."Id"
                    WHERE s."RevokedAt" IS NOT NULL
                      AND r."RevokedAt" IS NULL
                ) THEN
                    RAISE EXCEPTION 'A revoked session cannot have an active refresh token'
                        USING ERRCODE = '23514',
                              CONSTRAINT = 'CK_sessions_no_active_refresh_token_when_revoked';
                END IF;
                RETURN NULL;
            END
            $function$;

            CREATE CONSTRAINT TRIGGER CK_sessions_no_active_refresh_token_when_revoked
            AFTER INSERT OR UPDATE ON sessions
            DEFERRABLE INITIALLY DEFERRED
            FOR EACH ROW EXECUTE FUNCTION aegis_validate_session_revocation_consistency();

            CREATE CONSTRAINT TRIGGER CK_refresh_tokens_no_active_token_for_revoked_session
            AFTER INSERT OR UPDATE ON refresh_tokens
            DEFERRABLE INITIALLY DEFERRED
            FOR EACH ROW EXECUTE FUNCTION aegis_validate_session_revocation_consistency();
            """);
    }

    protected override void Down(MigrationBuilder m)
    {
        m.Sql("""
            DROP TRIGGER IF EXISTS CK_sessions_no_active_refresh_token_when_revoked ON sessions;
            DROP TRIGGER IF EXISTS CK_refresh_tokens_no_active_token_for_revoked_session ON refresh_tokens;
            DROP FUNCTION IF EXISTS aegis_validate_session_revocation_consistency();
            """);
        m.DropCheckConstraint("CK_sessions_revocation_reason_range", "sessions");
        m.DropCheckConstraint("CK_sessions_revocation_pair", "sessions");
    }
}
