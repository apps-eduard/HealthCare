using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationAuditAndUsageLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxClinics",
                schema: "public",
                table: "Organizations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxStaff",
                schema: "public",
                table: "Organizations",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OrganizationAuditEvents",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Action = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ResultCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResourceType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationAuditEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationAuditEvents_OrganizationId_Action_OccurredAtUtc",
                schema: "public",
                table: "OrganizationAuditEvents",
                columns: new[] { "OrganizationId", "Action", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationAuditEvents_OrganizationId_ActorUserId_Occurred~",
                schema: "public",
                table: "OrganizationAuditEvents",
                columns: new[] { "OrganizationId", "ActorUserId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationAuditEvents_OrganizationId_ClinicId_OccurredAtU~",
                schema: "public",
                table: "OrganizationAuditEvents",
                columns: new[] { "OrganizationId", "ClinicId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationAuditEvents_OrganizationId_CorrelationId",
                schema: "public",
                table: "OrganizationAuditEvents",
                columns: new[] { "OrganizationId", "CorrelationId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationAuditEvents_OrganizationId_OccurredAtUtc",
                schema: "public",
                table: "OrganizationAuditEvents",
                columns: new[] { "OrganizationId", "OccurredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrganizationAuditEvents",
                schema: "public");

            migrationBuilder.DropColumn(
                name: "MaxClinics",
                schema: "public",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "MaxStaff",
                schema: "public",
                table: "Organizations");
        }
    }
}
