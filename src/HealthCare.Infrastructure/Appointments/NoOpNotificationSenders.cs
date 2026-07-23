using HealthCare.Application.Appointments;
using HealthCare.Contracts.Appointments;
using HealthCare.Domain.Appointments;
using Microsoft.Extensions.Logging;

namespace HealthCare.Infrastructure.Appointments;

/// <summary>
/// Non-Development placeholder until a real notification provider is configured.
/// Completes successfully without delivering content.
/// </summary>
public sealed class NoOpAppointmentReminderSender : IAppointmentReminderSender
{
    private readonly ILogger<NoOpAppointmentReminderSender> _logger;

    public NoOpAppointmentReminderSender(ILogger<NoOpAppointmentReminderSender> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(AppointmentReminderDeliveryRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Reminder delivery skipped (no provider configured). ReminderId={ReminderId} AppointmentId={AppointmentId} Type={ReminderType}",
            request.ReminderId,
            request.AppointmentId,
            request.ReminderType);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Non-Development placeholder until a real notification provider is configured.
/// </summary>
public sealed class NoOpClinicAppointmentSummarySender : IClinicAppointmentSummarySender
{
    private readonly ILogger<NoOpClinicAppointmentSummarySender> _logger;

    public NoOpClinicAppointmentSummarySender(ILogger<NoOpClinicAppointmentSummarySender> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(ClinicAppointmentSummaryResponse summary, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Clinic summary delivery skipped (no provider configured). ClinicId={ClinicId} SummaryDate={SummaryDate} Total={Total}",
            summary.ClinicId,
            summary.SummaryDate,
            summary.TotalAppointments);
        return Task.CompletedTask;
    }
}
