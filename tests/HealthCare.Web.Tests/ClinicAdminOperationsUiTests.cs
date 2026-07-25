using FluentAssertions;
using HealthCare.Contracts.Appointments;
using HealthCare.Contracts.Identity;
using HealthCare.Web.Auth;
using HealthCare.Web.Operations;
using HealthCare.Web.Services;

namespace HealthCare.Web.Tests;

public sealed class ClinicAdminOperationsUiTests
{
    [Fact]
    public async Task Clinic_Admin_Sees_Operations_Nav_Without_Clinic_Picker_Or_Hangfire()
    {
        var state = await ClinicAdminStateAsync();

        OperationsPermissionRules.CanViewReminders(state).Should().BeTrue();
        OperationsPermissionRules.CanRetryReminders(state).Should().BeTrue();
        OperationsPermissionRules.CanViewSummaries(state).Should().BeTrue();
        OperationsPermissionRules.CanRetrySummaries(state).Should().BeTrue();
        OperationsPermissionRules.CanViewOperationsHealth(state).Should().BeTrue();
        OperationsPermissionRules.ShowClinicPicker(state).Should().BeFalse();
        OperationsPermissionRules.ShowHangfireInfrastructure(state).Should().BeFalse();

        OperationsPageCopy.RemindersSubtitle(state).Should().Contain("your clinic");
        OperationsPageCopy.SummariesSubtitle(state).Should().Contain("your clinic");
        OperationsPageCopy.HealthSubtitle(state).Should().Contain("Clinic-scoped");
        OperationsPageCopy.ClinicCaption(state, "East Clinic").Should().Be("East Clinic");

        state.Has(WebPermissions.OrganizationProfileRead).Should().BeFalse();
        state.Has("clinic_reports.read").Should().BeFalse();
        state.Has("clinic_audit_logs.read").Should().BeFalse();
        state.Has("hangfire.dashboard").Should().BeFalse();
    }

    [Fact]
    public async Task Organization_Admin_Keeps_Clinic_Picker_And_Hangfire_Flags()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "oa@test.local",
            Roles = [WebRoles.OrganizationAdmin],
            Permissions =
            [
                WebPermissions.RemindersRead,
                WebPermissions.RemindersRetry,
                WebPermissions.SummariesRead,
                WebPermissions.SummariesRetry,
                WebPermissions.ClinicsRead,
            ],
            HasActiveStaffMembership = true,
            OrganizationId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
        });

        OperationsPermissionRules.ShowClinicPicker(state).Should().BeTrue();
        OperationsPermissionRules.ShowHangfireInfrastructure(state).Should().BeTrue();
        OperationsPageCopy.RemindersSubtitle(state).Should().Contain("Organization");
        OperationsPageCopy.HealthSubtitle(state).Should().Contain("Hangfire");
    }

    [Fact]
    public async Task Platform_Admin_Keeps_Explicit_Context_Behavior()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "admin@test.local",
            Roles = [WebRoles.PlatformAdmin],
            Permissions =
            [
                WebPermissions.RemindersRead,
                WebPermissions.SummariesRead,
                WebPermissions.ClinicsRead,
            ],
            HasActiveStaffMembership = false,
        });

        OperationsPermissionRules.ShowClinicPicker(state).Should().BeTrue();
        OperationsPermissionRules.ShowHangfireInfrastructure(state).Should().BeTrue();
        OperationsPageCopy.RemindersSubtitle(state).Should().Contain("selected");
    }

    [Fact]
    public async Task Patient_Cannot_View_Operations()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "patient@test.local",
            Roles = [WebRoles.Patient],
            Permissions = [],
            HasActiveStaffMembership = false,
        });

        OperationsPermissionRules.CanViewAnyOperations(state).Should().BeFalse();
        OperationsPermissionRules.CanViewReminders(state).Should().BeFalse();
        OperationsPermissionRules.CanViewSummaries(state).Should().BeFalse();
    }

    [Theory]
    [InlineData("Failed", true)]
    [InlineData("Pending", true)]
    [InlineData("Sent", false)]
    [InlineData("Processing", false)]
    public void Retry_Hints_Match_Eligible_Statuses(string status, bool expected)
    {
        ReminderStatusPresentation.AppearsRetryable(status).Should().Be(expected);
        if (status is "Failed" or "Pending" or "Processing")
        {
            SummaryRunStatusPresentation.AppearsRetryable(status).Should().Be(expected);
        }
    }

    [Fact]
    public void Operations_Pages_Are_Actor_Aware_For_Clinic_Admin()
    {
        var webRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "HealthCare.Web"));
        var reminders = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "OperationsReminders.razor"));
        var summaries = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "OperationsClinicSummaries.razor"));
        var health = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "OperationsHealth.razor"));
        var layout = File.ReadAllText(Path.Combine(webRoot, "Components", "Layout", "StaffLayout.razor"));
        var detail = File.ReadAllText(Path.Combine(webRoot, "Components", "Operations", "ReminderDetailDialog.razor"));

        reminders.Should().Contain("OperationsPageCopy.RemindersSubtitle");
        reminders.Should().Contain("OperationsPermissionRules.ShowClinicPicker");
        reminders.Should().Contain("operations-reminders-clinic-caption");
        reminders.Should().Contain("_retryBusy");
        reminders.Should().Contain("queued for retry");
        reminders.Should().NotContain("@inject HttpClient");
        reminders.Should().NotContain("MedicalNote");
        reminders.Should().NotContain("/hangfire");

        summaries.Should().Contain("OperationsPageCopy.SummariesSubtitle");
        summaries.Should().Contain("operations-summaries-clinic-caption");
        summaries.Should().Contain("_retryBusy");

        health.Should().Contain("OperationsPageCopy.HealthSubtitle");
        health.Should().Contain("operations-health-clinic-caption");
        health.Should().Contain("ShowHangfireInfrastructure");
        health.Should().Contain("FailedReminderCount");
        health.Should().Contain("View reminders");
        health.Should().NotContain("@inject HttpClient");

        layout.Should().Contain("/operations/reminders");
        layout.Should().Contain("/operations/clinic-summaries");
        layout.Should().Contain("/operations/health");
        layout.Should().NotContain("clinic_reports");
        layout.Should().NotContain("/hangfire");

        detail.Should().Contain("_busy");
        detail.Should().Contain("provider secrets");
        detail.Should().NotContain("message body");
    }

    [Fact]
    public void Safe_Problem_And_Health_Contracts()
    {
        ReminderProblemMessages.ToUserMessage(new ApiProblemException(
            403, "Forbidden", "raw", "authorization.permission_denied"))
            .Should().Contain("permission").And.NotContain("raw");
        ReminderProblemMessages.ToUserMessage(new ApiProblemException(
            404, "Not Found", "x", "appointment.reminder_not_found"))
            .Should().Contain("not found");
        ReminderProblemMessages.IsRetryConflict(new ApiProblemException(
            409, "Conflict", null, "appointment.reminder_not_retryable")).Should().BeTrue();

        SummaryProblemMessages.ToUserMessage(new ApiProblemException(
            409, "Conflict", "stack", "appointment.summary_already_completed"))
            .Should().Contain("already completed").And.NotContain("stack");

        var health = new StaffOperationsHealthResponse
        {
            ReminderSenderMode = "Development",
            SummarySenderMode = "Development",
            FailedReminderCount = 2,
            PendingReminderCount = 1,
            FailedSummaryRunCount = 0,
            ClinicId = Guid.NewGuid(),
            ClinicName = "Clinic A",
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            HangfireQueues = ["reminders"],
        };
        typeof(StaffOperationsHealthResponse).GetProperty("ConnectionString").Should().BeNull();
        typeof(StaffOperationsHealthResponse).GetProperty("ApiKey").Should().BeNull();
        health.FailedReminderCount.Should().Be(2);
    }

    private static async Task<PermissionState> ClinicAdminStateAsync()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "clinicadmin@test.local",
            Roles = [WebRoles.ClinicAdmin],
            Permissions =
            [
                WebPermissions.RemindersRead,
                WebPermissions.RemindersRetry,
                WebPermissions.SummariesRead,
                WebPermissions.SummariesRetry,
            ],
            HasActiveStaffMembership = true,
            OrganizationId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
        });
        return state;
    }
}
