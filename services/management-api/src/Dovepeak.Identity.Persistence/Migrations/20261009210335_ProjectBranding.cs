using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dovepeak.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProjectBranding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "brand_logo_url",
                table: "projects",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "brand_primary_color",
                table: "projects",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "email_verification_intro",
                table: "projects",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "email_verification_subject",
                table: "projects",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "password_reset_intro",
                table: "projects",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "password_reset_subject",
                table: "projects",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "brand_logo_url",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "brand_primary_color",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "email_verification_intro",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "email_verification_subject",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "password_reset_intro",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "password_reset_subject",
                table: "projects");
        }
    }
}
