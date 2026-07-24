using HealthCare.Application.Appointments;
using HealthCare.Application.Authorization;
using HealthCare.Contracts.Appointments;
using HealthCare.Domain.Identity;
using HealthCare.Infrastructure.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace HealthCare.Infrastructure.Appointments;

public sealed class StaffOperationsHealthService : IStaffOperationsHealthService
{
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentStaff _currentStaff;
    private readonly IAppointmentReminderSender _reminderSender;
    private readonly IClinicAppointmentSummarySender _summarySender;
    private readonly IOptions<HangfireOptions> _hangfire;
    private readonly IHostEnvironment _environment;
    private readonly IAuthorizationAuditLogger _audit;

    public StaffOperationsHealthService(
        ICurrentUser currentUser,
        ICurrentStaff currentStaff,
        IAppointmentReminderSender reminderSender,
        IClinicAppointmentSummarySender summarySender,
        IOptions<HangfireOptions> hangfire,
        IHostEnvironment environment,
        IAuthorizationAuditLogger audit)
    {
        _currentUser = currentUser;
        _currentStaff = currentStaff;
        _reminderSender = reminderSender;
        _summarySender = summarySender;
        _hangfire = hangfire;
        _environment = environment;
        _audit = audit;
    }

    public Task<StaffOperationsHealthResponse> GetHealthAsync(
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        if (!_currentUser.IsAuthenticated)
        {
            throw AuthorizationException.NotAuthenticated();
        }

        if (_currentUser.IsInRole(AppRoles.Patient) && !_currentStaff.HasActiveMembership)
        {
            throw AuthorizationException.Forbidden();
        }

        if (bypass == PlatformAdminBypass.Explicit && _currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            _audit.ExplicitPlatformBypassUsed("operations_health", null, null);
        }
        else if (!_currentStaff.HasActiveMembership)
        {
            throw AuthorizationException.MissingStaffMembership();
        }

        var options = _hangfire.Value;
        var response = new StaffOperationsHealthResponse
        {
            ReminderSenderMode = DescribeSender(_reminderSender),
            SummarySenderMode = DescribeSender(_summarySender),
            HangfireWorkersEnabled = options.Enabled,
            HangfireRecurringJobsScheduled = options.ScheduleRecurringJobs,
            HangfireDashboardEnabled = options.Dashboard.Enabled,
            HangfireQueues = options.Queues?.ToArray() ?? [],
        };

        _audit.ReminderOperation(
            "operations_health",
            "succeeded",
            _currentStaff.HasActiveMembership ? _currentStaff.OrganizationId : null,
            _currentStaff.HasActiveMembership ? _currentStaff.ClinicId : null,
            reminderId: null);

        return Task.FromResult(response);
    }

    private string DescribeSender(object sender)
    {
        var name = sender.GetType().Name;
        if (name.Contains("Development", StringComparison.OrdinalIgnoreCase))
        {
            return _environment.IsDevelopment() ? "Development" : "DevelopmentUnexpected";
        }

        if (name.Contains("NoOp", StringComparison.OrdinalIgnoreCase))
        {
            return "Disabled";
        }

        return "Configured";
    }
}
