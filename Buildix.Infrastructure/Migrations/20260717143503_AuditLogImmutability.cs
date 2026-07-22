using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Buildix.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AuditLogImmutability : Migration
    {
        // Makes the audit trail genuinely append-only at the database layer.
        // AppDbContext documents that AuditLog / DebtAuditLog rows are protected
        // by "the append-only trigger added in the AuditLogImmutability
        // migration" and relies on it to justify DeleteBehavior.Restrict on the
        // audit FKs — but the trigger never existed, so until now the rows were
        // UPDATE/DELETE-able and the tamper-proof guarantee was only convention.
        // A BEFORE UPDATE OR DELETE trigger closes that gap: no application bug,
        // rogue query, or FK-cascade can rewrite or remove an audit record.
        // INSERT is untouched, so writing new audit entries works as before.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION buildix_prevent_audit_mutation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    RAISE EXCEPTION 'Audit rows are append-only: % on % is not permitted', TG_OP, TG_TABLE_NAME
                        USING ERRCODE = 'check_violation';
                END;
                $$;");

            migrationBuilder.Sql(@"
                CREATE TRIGGER trg_auditlogs_append_only
                    BEFORE UPDATE OR DELETE ON ""AuditLogs""
                    FOR EACH ROW EXECUTE FUNCTION buildix_prevent_audit_mutation();");

            migrationBuilder.Sql(@"
                CREATE TRIGGER trg_debtauditlogs_append_only
                    BEFORE UPDATE OR DELETE ON ""DebtAuditLogs""
                    FOR EACH ROW EXECUTE FUNCTION buildix_prevent_audit_mutation();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS trg_auditlogs_append_only ON ""AuditLogs"";");
            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS trg_debtauditlogs_append_only ON ""DebtAuditLogs"";");
            migrationBuilder.Sql(@"DROP FUNCTION IF EXISTS buildix_prevent_audit_mutation();");
        }
    }
}
