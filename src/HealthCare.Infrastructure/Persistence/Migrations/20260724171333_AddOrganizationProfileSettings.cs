using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationProfileSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BrandingPlaceholder",
                schema: "public",
                table: "Organizations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactEmail",
                schema: "public",
                table: "Organizations",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactPhone",
                schema: "public",
                table: "Organizations",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Country",
                schema: "public",
                table: "Organizations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultTimeZoneId",
                schema: "public",
                table: "Organizations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                schema: "public",
                table: "Organizations",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BrandingPlaceholder",
                schema: "public",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "ContactEmail",
                schema: "public",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "ContactPhone",
                schema: "public",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "Country",
                schema: "public",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "DefaultTimeZoneId",
                schema: "public",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "Version",
                schema: "public",
                table: "Organizations");
        }
    }
}
