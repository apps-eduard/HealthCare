using FluentAssertions;
using HealthCare.Contracts.Appointments;
using HealthCare.Contracts.Identity;
using HealthCare.Web.Auth;
using HealthCare.Web.Operations;
using HealthCare.Web.Services;

namespace HealthCare.Web.Tests;

public sealed class OrganizationAdminOperationsUiTests
{
    [Fact]
    public async Task Organization_Admin_Can_View_And_Retry_Reminders_And_Summaries()
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
            ],
            HasActiveStaffMembership = true,
            OrganizationId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
        });

        OperationsPermissionRules.CanViewReminders(state).Should().BeTrue();
        OperationsPermissionRules.CanRetryReminders(state).Should().BeTrue();
        OperationsPermissionRules.CanViewSummaries(state).Should().BeTrue();
        OperationsPermissionRules.CanRetrySummaries(state).Should().BeTrue();
        OperationsPermissionRules.CanViewOperationsHealth(state).Should().BeTrue();
        OperationsPermissionRules.CanViewAnyOperations(state).Should().BeTrue();
    }

    [Fact]
    public async Task Read_Only_Reminder_Permission_Does_Not_Allow_Retry()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "doc@test.local",
            Roles = ["DOCTOR"],
            Permissions = [WebPermissions.RemindersRead],
            HasActiveStaffMembership = true,
            OrganizationId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
        });

        OperationsPermissionRules.CanViewReminders(state).Should().BeTrue();
        OperationsPermissionRules.CanRetryReminders(state).Should().BeFalse();
        OperationsPermissionRules.CanViewOperationsHealth(state).Should().BeTrue();
    }

    [Theory]
    [InlineData("Failed", true)]
    [InlineData("Pending", true)]
    [InlineData("Sent", false)]
    [InlineData("Processing", false)]
    [InlineData("Cancelled", false)]
    public void Reminder_Retry_Hint_Matches_Backend_Eligible_Statuses(string status, bool expected) =>
        ReminderStatusPresentation.AppearsRetryable(status).Should().Be(expected);

    [Theory]
    [InlineData("Failed", true)]
    [InlineData("Pending", true)]
    [InlineData("Completed", false)]
    [InlineData("Processing", false)]
    public void Summary_Retry_Hint_Matches_Backend_Eligible_Statuses(string status, bool expected) =>
        SummaryRunStatusPresentation.AppearsRetryable(status).Should().Be(expected);

    [Fact]
    public void Problem_Messages_Map_Retry_Conflicts_Safely()
    {
        var reminder = new ApiProblemException(409, "Conflict", "raw", AppointmentReminderErrorCodes.ReminderNotRetryable);
        ReminderProblemMessages.IsRetryConflict(reminder).Should().BeTrue();
        ReminderProblemMessages.ToUserMessage(reminder).Should().Contain("cannot be retried");
        ReminderProblemMessages.ToUserMessage(reminder).Should().NotContain("raw");

        var summary = new ApiProblemException(409, "Conflict", "stack", AppointmentSummaryErrorCodes.SummaryAlreadyCompleted);
        SummaryProblemMessages.IsRetryConflict(summary).Should().BeTrue();
        SummaryProblemMessages.ToUserMessage(summary).Should().Contain("already completed");
        SummaryProblemMessages.ToUserMessage(summary).Should().NotContain("stack");
    }

    [Fact]
    public void Operations_Pages_Use_Clinic_Context_And_Safe_Client()
    {
        var webRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "HealthCare.Web"));
        var reminders = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "OperationsReminders.razor"));
        var summaries = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "OperationsClinicSummaries.razor"));
        var health = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "OperationsHealth.razor"));
        var layout = File.ReadAllText(Path.Combine(webRoot, "Components", "Layout", "StaffLayout.razor"));
        var detail = File.ReadAllText(Path.Combine(webRoot, "Components", "Appointments", "AppointmentDetailDialog.razor"));

        reminders.Should().Contain("IClinicWorkingContext");
        reminders.Should().Contain("SearchRemindersAsync");
        reminders.Should().Contain("RetryReminderAsync");
        reminders.Should().NotContain("@inject HttpClient");
        reminders.Should().NotContain("provider secret");

        summaries.Should().Contain("IClinicWorkingContext");
        summaries.Should().Contain("ListSummaryRunsAsync");
        summaries.Should().Contain("RetrySummaryAsync");

        health.Should().Contain("GetOperationsHealthAsync");
        health.Should().Contain("HangfireWorkersEnabled");
        health.Should().Contain("no secrets");

        layout.Should().Contain("Operations");
        layout.Should().Contain("/operations/reminders");
        layout.Should().Contain("/operations/clinic-summaries");
        layout.Should().Contain("/operations/health");

        detail.Should().Contain("ListAppointmentRemindersAsync");
        detail.Should().Contain("RetryReminderAsync");
    }

    [Fact]
    public void Operations_Client_Exposes_Reminder_Summary_And_Health_Routes()
    {
        var source = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "HealthCare.Web", "Services", "StaffOperationsApiClient.cs")));

        source.Should().Contain("api/v1/staff/reminders");
        source.Should().Contain("api/v1/staff/appointments/");
        source.Should().Contain("reminders/retry");
        source.Should().Contain("api/v1/staff/appointment-summary-runs");
        source.Should().Contain("appointment-summary/");
        source.Should().Contain("api/v1/staff/operations/health");
        source.Should().Contain("RetryAppointmentReminderRequest");
    }

    [Fact]
    public void Health_Contract_Excludes_Secrets_And_Payloads()
    {
        var health = new StaffOperationsHealthResponse
        {
            ReminderSenderMode = "Development",
            SummarySenderMode = "Development",
            HangfireWorkersEnabled = true,
            HangfireRecurringJobsScheduled = true,
            HangfireDashboardEnabled = false,
            HangfireQueues = ["reminders", "summaries"],
        };

        typeof(StaffOperationsHealthResponse).GetProperty("ConnectionString").Should().BeNull();
        typeof(StaffOperationsHealthResponse).GetProperty("ApiKey").Should().BeNull();
        typeof(StaffOperationsHealthResponse).GetProperty("JobArguments").Should().BeNull();
        health.HangfireQueues.Should().Equal("reminders", "summaries");
    }
}
