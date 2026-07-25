using FluentAssertions;
using HealthCare.Contracts.Identity;
using HealthCare.Web.Auth;
using HealthCare.Web.MedicalNotes;
using HealthCare.Web.Services;

namespace HealthCare.Web.Tests;

public sealed class MedicalNotesUiTests
{
    [Fact]
    public async Task Doctor_Has_Medical_Note_Permission_Gates()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "doctor@test.local",
            Roles = [WebRoles.Doctor],
            Permissions =
            [
                WebPermissions.MedicalNotesRead,
                WebPermissions.MedicalNotesCreate,
                WebPermissions.MedicalNotesUpdateDraft,
                WebPermissions.MedicalNotesSign,
                WebPermissions.MedicalNotesAmend,
            ],
            HasActiveStaffMembership = true,
            StaffMemberId = Guid.NewGuid(),
        });

        MedicalNotePermissionRules.CanView(state).Should().BeTrue();
        MedicalNotePermissionRules.CanCreate(state).Should().BeTrue();
        MedicalNotePermissionRules.CanAmend(state).Should().BeTrue();
    }

    [Fact]
    public async Task Clinic_Admin_Does_Not_Get_Note_Gates()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "ca@test.local",
            Roles = [WebRoles.ClinicAdmin],
            Permissions = [WebPermissions.AppointmentsRead],
            HasActiveStaffMembership = true,
        });

        MedicalNotePermissionRules.CanView(state).Should().BeFalse();
        MedicalNotePermissionRules.CanCreate(state).Should().BeFalse();
    }

    [Fact]
    public void Appointment_Detail_Hosts_Note_Ux_Without_Global_Nav()
    {
        var detail = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "src", "HealthCare.Web", "Components", "Appointments", "AppointmentDetailDialog.razor"));
        detail.Should().Contain("MedicalNotePermissionRules");
        detail.Should().Contain("IMedicalNoteApiClient");
        detail.Should().Contain("New draft note");

        var layout = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "src", "HealthCare.Web", "Components", "Layout", "StaffLayout.razor"));
        layout.Should().NotContain("/medical-notes");
        layout.ToLowerInvariant().Should().NotContain("medical notes");
    }

    [Fact]
    public void Medical_Note_Api_Client_Is_Registered()
    {
        typeof(IMedicalNoteApiClient).Should().NotBeNull();
        typeof(MedicalNoteApiClient).Should().NotBeNull();
        var program = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "HealthCare.Web", "Program.cs"));
        program.Should().Contain("IMedicalNoteApiClient");
    }

    [Fact]
    public void Appointment_Detail_Selects_Draft_After_Create_Without_Busy_Gate()
    {
        var detail = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "src", "HealthCare.Web", "Components", "Appointments", "AppointmentDetailDialog.razor"));
        detail.Should().Contain("_selectedNote = created");
        detail.Should().Contain("private async Task SelectNoteAsync");
        detail.Should().Contain("medical-note-plan");
        detail.Should().Contain("for=\"medical-note-plan\"");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HealthCare.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
