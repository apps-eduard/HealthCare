using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentAvailability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                schema: "public",
                table: "Clinics",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "Asia/Riyadh");

            migrationBuilder.CreateTable(
                name: "DoctorAvailabilities",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    DoctorStaffMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    DayOfWeek = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    StartLocalTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    EndLocalTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    SlotDurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    Version = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DoctorAvailabilities", x => x.Id);
                    table.CheckConstraint("CK_DoctorAvailabilities_SlotDuration", "\"SlotDurationMinutes\" >= 10 AND \"SlotDurationMinutes\" <= 240");
                    table.CheckConstraint("CK_DoctorAvailabilities_TimeOrder", "\"StartLocalTime\" < \"EndLocalTime\"");
                    table.ForeignKey(
                        name: "FK_DoctorAvailabilities_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalSchema: "public",
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DoctorAvailabilities_StaffMembers_DoctorStaffMemberId",
                        column: x => x.DoctorStaffMemberId,
                        principalSchema: "public",
                        principalTable: "StaffMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DoctorAvailabilityExceptions",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    DoctorStaffMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    ExceptionType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StartLocalTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    EndLocalTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    Reason = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DoctorAvailabilityExceptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DoctorAvailabilityExceptions_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalSchema: "public",
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DoctorAvailabilityExceptions_StaffMembers_DoctorStaffMember~",
                        column: x => x.DoctorStaffMemberId,
                        principalSchema: "public",
                        principalTable: "StaffMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DoctorAvailabilities_ClinicId_DoctorStaffMemberId",
                schema: "public",
                table: "DoctorAvailabilities",
                columns: new[] { "ClinicId", "DoctorStaffMemberId" });

            migrationBuilder.CreateIndex(
                name: "IX_DoctorAvailabilities_DoctorStaffMemberId_DayOfWeek_IsActive",
                schema: "public",
                table: "DoctorAvailabilities",
                columns: new[] { "DoctorStaffMemberId", "DayOfWeek", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_DoctorAvailabilities_OrganizationId",
                schema: "public",
                table: "DoctorAvailabilities",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_DoctorAvailabilityExceptions_ClinicId",
                schema: "public",
                table: "DoctorAvailabilityExceptions",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_DoctorAvailabilityExceptions_DoctorStaffMemberId_Date",
                schema: "public",
                table: "DoctorAvailabilityExceptions",
                columns: new[] { "DoctorStaffMemberId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_DoctorAvailabilityExceptions_OrganizationId",
                schema: "public",
                table: "DoctorAvailabilityExceptions",
                column: "OrganizationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DoctorAvailabilities",
                schema: "public");

            migrationBuilder.DropTable(
                name: "DoctorAvailabilityExceptions",
                schema: "public");

            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                schema: "public",
                table: "Clinics");
        }
    }
}
