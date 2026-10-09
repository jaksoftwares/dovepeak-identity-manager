using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dovepeak.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAuditStore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_checkpoints",
                columns: table => new
                {
                    realm = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    last_event_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_checkpoints", x => new { x.realm, x.source });
                });

            migrationBuilder.CreateTable(
                name: "audit_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    realm = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source_event_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    user_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    client_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    session_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    error = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    details = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_events", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_realm_occurred_at",
                table: "audit_events",
                columns: new[] { "realm", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_realm_source_source_event_id",
                table: "audit_events",
                columns: new[] { "realm", "source", "source_event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_realm_user_id_occurred_at",
                table: "audit_events",
                columns: new[] { "realm", "user_id", "occurred_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_checkpoints");

            migrationBuilder.DropTable(
                name: "audit_events");
        }
    }
}
