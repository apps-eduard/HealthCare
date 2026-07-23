using HealthCare.Application.Appointments;
using HealthCare.Domain.Appointments;
using Microsoft.Extensions.Logging;

namespace HealthCare.Infrastructure.Appointments;

/// <summary>
/// Development-only sender: logs a safe summary without patient PII or appointment reasons.
/// </summary>
public sealed class DevelopmentAppointmentReminderSender : IAppointmentReminderSender
{
    private readonly ILogger<DevelopmentAppointmentReminderSender> _logger;

    public DevelopmentAppointmentReminderSender(ILogger<DevelopmentAppointmentReminderSender> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(AppointmentReminderDeliveryRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Reminder delivered (Development). ReminderId={ReminderId} AppointmentId={AppointmentId} Type={ReminderType} ClinicCode={ClinicCode} LocalDisplay={LocalDisplay} TimeZoneId={TimeZoneId}",
            request.ReminderId,
            request.AppointmentId,
            request.ReminderType,
            request.ClinicCode,
            request.AppointmentLocalDisplay,
            request.TimeZoneId);

        return Task.CompletedTask;
    }
}
