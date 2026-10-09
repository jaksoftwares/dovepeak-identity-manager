using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dovepeak.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnvironmentScopes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<string>>(
                name: "scopes",
                table: "applications",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.CreateTable(
                name: "environment_scopes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    engine_scope_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_environment_scopes", x => x.id);
                    table.ForeignKey(
                        name: "fk_environment_scopes_environments_environment_id",
                        column: x => x.environment_id,
                        principalTable: "environments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_environment_scopes_environment_id_name",
                table: "environment_scopes",
                columns: new[] { "environment_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_environment_scopes_organization_id",
                table: "environment_scopes",
                column: "organization_id");

            // Same row-level security policy as every other tenant table (MultiTenancy migration, ADR-0008).
            migrationBuilder.Sql("ALTER TABLE environment_scopes ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE environment_scopes FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON environment_scopes USING (" +
                "current_setting('dovepeak.system', true) = 'on' " +
                "OR organization_id = NULLIF(current_setting('dovepeak.org_id', true), '')::uuid) WITH CHECK (" +
                "current_setting('dovepeak.system', true) = 'on' " +
                "OR organization_id = NULLIF(current_setting('dovepeak.org_id', true), '')::uuid);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "environment_scopes");

            migrationBuilder.DropColumn(
                name: "scopes",
                table: "applications");
        }
    }
}
