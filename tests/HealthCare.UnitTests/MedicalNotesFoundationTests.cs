using FluentAssertions;
using HealthCare.Api.Controllers;
using HealthCare.Application.Authorization;
using HealthCare.Application.MedicalNotes;
using HealthCare.Contracts.MedicalNotes;
using HealthCare.Domain.Appointments;
using HealthCare.Domain.Identity;
using HealthCare.Domain.MedicalNotes;
using HealthCare.Infrastructure.MedicalNotes;
using HealthCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HealthCare.UnitTests;

public sealed class MedicalNotesFoundationTests
{
    [Fact]
    public void Doctor_Has_All_Medical_Note_Permissions()
    {
        var permissions = RolePermissionMatrix.GetPermissionsForRole(AppRoles.Doctor);

        permissions.Should().Contain([
            Permissions.MedicalNotes.Read,
            Permissions.MedicalNotes.Create,
            Permissions.MedicalNotes.UpdateDraft,
            Permissions.MedicalNotes.Sign,
            Permissions.MedicalNotes.Amend,
        ]);
    }

    [Theory]
    [InlineData(AppRoles.Receptionist)]
    [InlineData(AppRoles.Patient)]
    [InlineData(AppRoles.ClinicAdmin)]
    [InlineData(AppRoles.OrganizationAdmin)]
    [InlineData(AppRoles.PlatformAdmin)]
    public void Nonclinical_And_Admin_Roles_Have_No_Medical_Note_Permissions(string role)
    {
        RolePermissionMatrix.GetPermissionsForRole(role)
            .Should().NotContain(p => p.StartsWith("medical_notes.", StringComparison.Ordinal));
    }

    [Fact]
    public void Clinical_Role_Is_Required_Even_With_Active_Staff_Membership()
    {
        var user = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            Roles = [AppRoles.ClinicAdmin],
        };
        var staff = new FakeCurrentStaff
        {
            HasActiveMembership = true,
            StaffMemberId = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
            Role = AppRoles.ClinicAdmin,
        };
        var sut = new MedicalNoteAccessService(user, staff, new NoOpAuthorizationAuditLogger());

        var act = () => sut.EnsureClinicalStaffForNotes();

        act.Should().Throw<MedicalNoteException>()
            .Which.ErrorCode.Should().Be(MedicalNoteErrorCodes.ClinicalRoleRequired);
    }

    [Theory]
    [InlineData(AppointmentStatus.CheckedIn)]
    [InlineData(AppointmentStatus.InProgress)]
    [InlineData(AppointmentStatus.Completed)]
    public async Task Doctor_Creates_Draft_For_Eligible_Appointment(AppointmentStatus status)
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var appointment = await h.SeedAppointmentAsync(data, status);
        var sut = h.CreateMedicalNoteService(
            data.DoctorAUserId, data.Org1Id, data.ClinicAId, data.DoctorAStaffId, AppRoles.Doctor);

        var result = await sut.CreateDraftAsync(appointment.Id, CreateDraft());

        result.Status.Should().Be(nameof(MedicalNoteStatus.Draft));
        result.Subjective.Should().Be("Clinical history");
        result.PatientId.Should().Be(data.PatientId);
    }

    [Fact]
    public async Task Create_Draft_For_Ineligible_Appointment_Is_Rejected()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var appointment = await h.SeedAppointmentAsync(data, AppointmentStatus.Confirmed);
        var sut = h.CreateMedicalNoteService(
            data.DoctorAUserId, data.Org1Id, data.ClinicAId, data.DoctorAStaffId, AppRoles.Doctor);

        var act = () => sut.CreateDraftAsync(appointment.Id, CreateDraft());

        await act.Should().ThrowAsync<MedicalNoteException>()
            .Where(e => e.ErrorCode == MedicalNoteErrorCodes.InvalidAppointmentState);
    }

    [Fact]
    public async Task Nurse_Can_Only_Create_And_Sign_Nursing_Notes()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var appointment = await h.SeedAppointmentAsync(data);
        var nurseUserId = Guid.NewGuid();
        var nurseStaffId = Guid.NewGuid();
        h.Db.StaffMembers.Add(new Domain.Staff.StaffMember
        {
            Id = nurseStaffId,
            UserId = nurseUserId,
            OrganizationId = data.Org1Id,
            ClinicId = data.ClinicAId,
            Role = AppRoles.Nurse,
            FirstName = "Nurse",
            LastName = "A",
            IsActive = true,
        });
        await h.Db.SaveChangesAsync();
        var sut = h.CreateMedicalNoteService(
            nurseUserId, data.Org1Id, data.ClinicAId, nurseStaffId, AppRoles.Nurse);

        var rejected = () => sut.CreateDraftAsync(appointment.Id, CreateDraft("Progress"));
        await rejected.Should().ThrowAsync<MedicalNoteException>()
            .Where(e => e.ErrorCode == MedicalNoteErrorCodes.NoteTypeNotAllowed);

        var note = await sut.CreateDraftAsync(appointment.Id, CreateDraft("Nursing"));
        var signed = await sut.SignAsync(note.Id, new SignMedicalNoteRequest { ExpectedVersion = note.Version });

        signed.Status.Should().Be(nameof(MedicalNoteStatus.Signed));
    }

    [Fact]
    public async Task Cross_Clinic_Note_Access_Is_NotFound()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var appointment = await h.SeedAppointmentAsync(data);
        var sut = h.CreateMedicalNoteService(
            data.DoctorBUserId, data.Org1Id, data.ClinicBId, data.DoctorBStaffId, AppRoles.Doctor);

        var act = () => sut.CreateDraftAsync(appointment.Id, CreateDraft());

        await act.Should().ThrowAsync<MedicalNoteException>()
            .Where(e => e.ErrorCode == MedicalNoteErrorCodes.NotFound);
    }

    [Fact]
    public async Task Only_Author_Can_Update_Or_Sign_Draft()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var appointment = await h.SeedAppointmentAsync(data);
        var author = h.CreateMedicalNoteService(
            data.DoctorAUserId, data.Org1Id, data.ClinicAId, data.DoctorAStaffId, AppRoles.Doctor);
        var note = await author.CreateDraftAsync(appointment.Id, CreateDraft());
        var otherAuthor = h.CreateMedicalNoteService(
            Guid.NewGuid(), data.Org1Id, data.ClinicAId, Guid.NewGuid(), AppRoles.Doctor);

        var update = () => otherAuthor.UpdateDraftAsync(
            note.Id, new UpdateMedicalNoteDraftRequest { ExpectedVersion = note.Version, Plan = "Different plan" });
        var sign = () => otherAuthor.SignAsync(note.Id, new SignMedicalNoteRequest { ExpectedVersion = note.Version });

        await update.Should().ThrowAsync<MedicalNoteException>()
            .Where(e => e.ErrorCode == MedicalNoteErrorCodes.AuthorRequired);
        await sign.Should().ThrowAsync<MedicalNoteException>()
            .Where(e => e.ErrorCode == MedicalNoteErrorCodes.AuthorRequired);
    }

    [Fact]
    public async Task Draft_Lifecycle_Enforces_Concurrency_And_Signed_Immutability()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var appointment = await h.SeedAppointmentAsync(data);
        var sut = h.CreateMedicalNoteService(
            data.DoctorAUserId, data.Org1Id, data.ClinicAId, data.DoctorAStaffId, AppRoles.Doctor);
        var note = await sut.CreateDraftAsync(appointment.Id, CreateDraft());

        var stale = () => sut.UpdateDraftAsync(note.Id, new UpdateMedicalNoteDraftRequest
        {
            ExpectedVersion = note.Version + 1,
            Plan = "Updated plan",
        });
        await stale.Should().ThrowAsync<MedicalNoteException>()
            .Where(e => e.ErrorCode == MedicalNoteErrorCodes.ConcurrencyConflict);

        var updated = await sut.UpdateDraftAsync(note.Id, new UpdateMedicalNoteDraftRequest
        {
            ExpectedVersion = note.Version,
            Plan = "Updated plan",
        });
        var signed = await sut.SignAsync(note.Id, new SignMedicalNoteRequest { ExpectedVersion = updated.Version });
        var afterSign = () => sut.UpdateDraftAsync(note.Id, new UpdateMedicalNoteDraftRequest
        {
            ExpectedVersion = signed.Version,
            Plan = "Not allowed",
        });

        signed.Status.Should().Be(nameof(MedicalNoteStatus.Signed));
        await afterSign.Should().ThrowAsync<MedicalNoteException>()
            .Where(e => e.ErrorCode == MedicalNoteErrorCodes.NotDraft);
    }

    [Fact]
    public async Task Doctor_Amendment_Is_Signed_Linked_And_Preserves_Original()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var appointment = await h.SeedAppointmentAsync(data);
        var sut = h.CreateMedicalNoteService(
            data.DoctorAUserId, data.Org1Id, data.ClinicAId, data.DoctorAStaffId, AppRoles.Doctor);
        var draft = await sut.CreateDraftAsync(appointment.Id, CreateDraft());
        var signed = await sut.SignAsync(draft.Id, new SignMedicalNoteRequest { ExpectedVersion = draft.Version });

        var amendment = await sut.AmendAsync(signed.Id, new AmendMedicalNoteRequest
        {
            ExpectedVersion = signed.Version,
            AmendmentReason = "Correction",
            Plan = "Corrected plan",
        });

        var original = await h.Db.MedicalNotes.AsNoTracking().SingleAsync(n => n.Id == signed.Id);
        amendment.Status.Should().Be(nameof(MedicalNoteStatus.Signed));
        amendment.AmendsMedicalNoteId.Should().Be(signed.Id);
        original.Plan.Should().Be("Care plan");
        h.Db.MedicalNotes.Should().HaveCount(2);
    }

    [Fact]
    public async Task List_Uses_Summary_Without_Soap_Content_And_Audits_Metadata_Only()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var appointment = await h.SeedAppointmentAsync(data);
        var sut = h.CreateMedicalNoteService(
            data.DoctorAUserId, data.Org1Id, data.ClinicAId, data.DoctorAStaffId, AppRoles.Doctor);
        await sut.CreateDraftAsync(appointment.Id, CreateDraft());

        var list = await sut.ListForAppointmentAsync(appointment.Id);

        list.Should().ContainSingle();
        typeof(MedicalNoteSummaryResponse).GetProperty(nameof(MedicalNoteDetailResponse.Subjective)).Should().BeNull();
        typeof(MedicalNoteSummaryResponse).GetProperty(nameof(MedicalNoteDetailResponse.Plan)).Should().BeNull();
        var audit = await h.Db.MedicalNoteAuditEvents
            .SingleAsync(e => e.Operation == "medical_note.list");
        audit.Operation.Should().Be("medical_note.list");
        typeof(MedicalNoteAuditEvent).GetProperty(nameof(MedicalNote.Subjective)).Should().BeNull();
        typeof(MedicalNoteAuditEvent).GetProperty(nameof(MedicalNote.Plan)).Should().BeNull();
    }

    [Fact]
    public async Task Medical_Note_Appointment_Foreign_Key_Is_Restrict()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var foreignKey = h.Db.Model.FindEntityType(typeof(MedicalNote))!
            .GetForeignKeys()
            .Single(fk => fk.PrincipalEntityType.ClrType == typeof(Appointment));

        foreignKey.DeleteBehavior.Should().Be(DeleteBehavior.Restrict);
    }

    [Fact]
    public void Medical_Notes_Controller_Does_Not_Depend_On_DbContext()
    {
        typeof(MedicalNotesController).GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType)
            .Should().NotContain(typeof(HealthCareDbContext));
    }

    private static CreateMedicalNoteDraftRequest CreateDraft(string noteType = "Progress") =>
        new()
        {
            NoteType = noteType,
            Subjective = "  Clinical history  ",
            Assessment = "Assessment",
            Plan = "Care plan",
        };
}
