using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStaffManagementFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                schema: "public",
                table: "StaffMembers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FirstName",
                schema: "public",
                table: "StaffMembers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "LastName",
                schema: "public",
                table: "StaffMembers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Version",
                schema: "public",
                table: "StaffMembers",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DisplayName",
                schema: "public",
                table: "StaffMembers");

            migrationBuilder.DropColumn(
                name: "FirstName",
                schema: "public",
                table: "StaffMembers");

            migrationBuilder.DropColumn(
                name: "LastName",
                schema: "public",
                table: "StaffMembers");

            migrationBuilder.DropColumn(
                name: "Version",
                schema: "public",
                table: "StaffMembers");
        }
    }
}
