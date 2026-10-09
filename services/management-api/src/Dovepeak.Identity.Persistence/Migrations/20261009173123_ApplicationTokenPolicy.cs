using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dovepeak.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ApplicationTokenPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "access_token_lifetime_seconds",
                table: "applications",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "session_idle_timeout_seconds",
                table: "applications",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "session_max_lifetime_seconds",
                table: "applications",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "access_token_lifetime_seconds",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "session_idle_timeout_seconds",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "session_max_lifetime_seconds",
                table: "applications");
        }
    }
}
