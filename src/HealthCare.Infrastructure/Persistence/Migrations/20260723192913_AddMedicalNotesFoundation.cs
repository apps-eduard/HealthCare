using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMedicalNotesFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MedicalNoteAuditEvents",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MedicalNoteId = table.Column<Guid>(type: "uuid", nullable: true),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActingUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActingStaffMemberId = table.Column<Guid>(type: "uuid", nullable: true),
                    Operation = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResultCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MedicalNoteAuditEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MedicalNotes",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicPatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorStaffMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    NoteType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Subjective = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    Objective = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    Assessment = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    Plan = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    AdditionalText = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SignedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SignedByStaffMemberId = table.Column<Guid>(type: "uuid", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    AmendsMedicalNoteId = table.Column<Guid>(type: "uuid", nullable: true),
                    AmendmentReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MedicalNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MedicalNotes_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalSchema: "public",
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MedicalNotes_ClinicPatients_ClinicPatientId",
                        column: x => x.ClinicPatientId,
                        principalSchema: "public",
                        principalTable: "ClinicPatients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MedicalNotes_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalSchema: "public",
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MedicalNotes_MedicalNotes_AmendsMedicalNoteId",
                        column: x => x.AmendsMedicalNoteId,
                        principalSchema: "public",
                        principalTable: "MedicalNotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MedicalNotes_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "public",
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MedicalNotes_Patients_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "public",
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MedicalNotes_StaffMembers_AuthorStaffMemberId",
                        column: x => x.AuthorStaffMemberId,
                        principalSchema: "public",
                        principalTable: "StaffMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MedicalNotes_StaffMembers_SignedByStaffMemberId",
                        column: x => x.SignedByStaffMemberId,
                        principalSchema: "public",
                        principalTable: "StaffMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MedicalNoteAuditEvents_ActingUserId_CreatedAtUtc",
                schema: "public",
                table: "MedicalNoteAuditEvents",
                columns: new[] { "ActingUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MedicalNoteAuditEvents_AppointmentId_CreatedAtUtc",
                schema: "public",
                table: "MedicalNoteAuditEvents",
                columns: new[] { "AppointmentId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MedicalNoteAuditEvents_CreatedAtUtc",
                schema: "public",
                table: "MedicalNoteAuditEvents",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MedicalNoteAuditEvents_MedicalNoteId_CreatedAtUtc",
                schema: "public",
                table: "MedicalNoteAuditEvents",
                columns: new[] { "MedicalNoteId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MedicalNotes_AmendsMedicalNoteId",
                schema: "public",
                table: "MedicalNotes",
                column: "AmendsMedicalNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_MedicalNotes_AppointmentId_CreatedAtUtc",
                schema: "public",
                table: "MedicalNotes",
                columns: new[] { "AppointmentId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MedicalNotes_AuthorStaffMemberId",
                schema: "public",
                table: "MedicalNotes",
                column: "AuthorStaffMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_MedicalNotes_ClinicId_PatientId",
                schema: "public",
                table: "MedicalNotes",
                columns: new[] { "ClinicId", "PatientId" });

            migrationBuilder.CreateIndex(
                name: "IX_MedicalNotes_ClinicPatientId",
                schema: "public",
                table: "MedicalNotes",
                column: "ClinicPatientId");

            migrationBuilder.CreateIndex(
                name: "IX_MedicalNotes_OrganizationId",
                schema: "public",
                table: "MedicalNotes",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_MedicalNotes_PatientId_CreatedAtUtc",
                schema: "public",
                table: "MedicalNotes",
                columns: new[] { "PatientId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MedicalNotes_SignedByStaffMemberId",
                schema: "public",
                table: "MedicalNotes",
                column: "SignedByStaffMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_MedicalNotes_Status",
                schema: "public",
                table: "MedicalNotes",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MedicalNoteAuditEvents",
                schema: "public");

            migrationBuilder.DropTable(
                name: "MedicalNotes",
                schema: "public");
        }
    }
}
