using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Appointments",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicPatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    DoctorStaffMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppointmentDateUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PatientNotes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Appointments", x => x.Id);
                    table.CheckConstraint("CK_Appointments_DurationMinutes", "\"DurationMinutes\" >= 5 AND \"DurationMinutes\" <= 480");
                    table.ForeignKey(
                        name: "FK_Appointments_ClinicPatients_ClinicPatientId",
                        column: x => x.ClinicPatientId,
                        principalSchema: "public",
                        principalTable: "ClinicPatients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Appointments_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalSchema: "public",
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Appointments_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "public",
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Appointments_Patients_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "public",
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Appointments_StaffMembers_DoctorStaffMemberId",
                        column: x => x.DoctorStaffMemberId,
                        principalSchema: "public",
                        principalTable: "StaffMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ClinicId_AppointmentDateUtc",
                schema: "public",
                table: "Appointments",
                columns: new[] { "ClinicId", "AppointmentDateUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ClinicId_Status",
                schema: "public",
                table: "Appointments",
                columns: new[] { "ClinicId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ClinicPatientId",
                schema: "public",
                table: "Appointments",
                column: "ClinicPatientId");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_DoctorStaffMemberId_AppointmentDateUtc",
                schema: "public",
                table: "Appointments",
                columns: new[] { "DoctorStaffMemberId", "AppointmentDateUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_OrganizationId",
                schema: "public",
                table: "Appointments",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_PatientId_AppointmentDateUtc",
                schema: "public",
                table: "Appointments",
                columns: new[] { "PatientId", "AppointmentDateUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Appointments",
                schema: "public");
        }
    }
}
