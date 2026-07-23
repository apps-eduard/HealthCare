using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClinicPatientConcurrencyAndStaffSearchIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Version",
                schema: "public",
                table: "ClinicPatients",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_ClinicPatients_ClinicId_Status",
                schema: "public",
                table: "ClinicPatients",
                columns: new[] { "ClinicId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ClinicPatients_ClinicId_Status",
                schema: "public",
                table: "ClinicPatients");

            migrationBuilder.DropColumn(
                name: "Version",
                schema: "public",
                table: "ClinicPatients");
        }
    }
}
