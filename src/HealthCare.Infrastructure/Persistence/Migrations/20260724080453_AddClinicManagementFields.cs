using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClinicManagementFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AddressLine1",
                schema: "public",
                table: "Clinics",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressLine2",
                schema: "public",
                table: "Clinics",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Country",
                schema: "public",
                table: "Clinics",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostalCode",
                schema: "public",
                table: "Clinics",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Region",
                schema: "public",
                table: "Clinics",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                schema: "public",
                table: "Clinics",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                """
                UPDATE "Clinics"
                SET "AddressLine1" = "Address"
                WHERE "AddressLine1" IS NULL AND "Address" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AddressLine1",
                schema: "public",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "AddressLine2",
                schema: "public",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "Country",
                schema: "public",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "PostalCode",
                schema: "public",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "Region",
                schema: "public",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "Version",
                schema: "public",
                table: "Clinics");
        }
    }
}
