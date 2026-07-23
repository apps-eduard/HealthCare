using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentRescheduleWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppointmentRescheduleHistories",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreviousDoctorStaffMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    NewDoctorStaffMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreviousStartUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    NewStartUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PreviousDurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    NewDurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    RescheduledByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RescheduledAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    PreviousVersion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppointmentRescheduleHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppointmentRescheduleHistories_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalSchema: "public",
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentRescheduleHistories_AppointmentId",
                schema: "public",
                table: "AppointmentRescheduleHistories",
                column: "AppointmentId");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentRescheduleHistories_RescheduledAtUtc",
                schema: "public",
                table: "AppointmentRescheduleHistories",
                column: "RescheduledAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppointmentRescheduleHistories",
                schema: "public");
        }
    }
}
