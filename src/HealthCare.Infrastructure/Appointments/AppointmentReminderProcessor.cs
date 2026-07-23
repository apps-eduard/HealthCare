using HealthCare.Application.Appointments;
using HealthCare.Domain.Appointments;
using HealthCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HealthCare.Infrastructure.Appointments;

public sealed class AppointmentReminderProcessor : IAppointmentReminderProcessor
{
    private readonly HealthCareDbContext _dbContext;
    private readonly IAppointmentReminderSender _sender;
    private readonly IClinicTimeZoneConverter _timeZones;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AppointmentReminderProcessor> _logger;

    public AppointmentReminderProcessor(
        HealthCareDbContext dbContext,
        IAppointmentReminderSender sender,
        IClinicTimeZoneConverter timeZones,
        TimeProvider timeProvider,
        ILogger<AppointmentReminderProcessor> logger)
    {
        _dbContext = dbContext;
        _sender = sender;
        _timeZones = timeZones;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task ProcessReminderAsync(
        Guid appointmentId,
        Guid reminderId,
        CancellationToken cancellationToken = default)
    {
        var reminder = await _dbContext.AppointmentReminders
            .SingleOrDefaultAsync(r => r.Id == reminderId && r.AppointmentId == appointmentId, cancellationToken);

        if (reminder is null)
        {
            _logger.LogInformation(
                "Reminder skipped (missing). AppointmentId={AppointmentId} ReminderId={ReminderId}",
                appointmentId,
                reminderId);
            return;
        }

        if (reminder.Status is AppointmentReminderStatus.Sent or AppointmentReminderStatus.Cancelled)
        {
            return;
        }

        var appointment = await _dbContext.Appointments
            .AsNoTracking()
            .SingleOrDefaultAsync(a => a.Id == appointmentId, cancellationToken);

        if (appointment is null)
        {
            reminder.Status = AppointmentReminderStatus.Cancelled;
            reminder.LastError = "appointment_missing";
            await _dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        if (!IsDeliverable(appointment, reminder.ReminderType))
        {
            reminder.Status = AppointmentReminderStatus.Cancelled;
            reminder.LastError = "appointment_status_not_deliverable";
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Reminder cancelled (status). AppointmentId={AppointmentId} ReminderId={ReminderId} AppointmentStatus={Status}",
                appointmentId,
                reminderId,
                appointment.Status);
            return;
        }

        reminder.Status = AppointmentReminderStatus.Processing;
        reminder.AttemptCount++;
        await _dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var clinic = await _dbContext.Clinics.AsNoTracking()
                .SingleAsync(c => c.Id == appointment.ClinicId, cancellationToken);

            var local = _timeZones.ToClinicLocal(appointment.AppointmentDateUtc, clinic.TimeZoneId);
            var delivery = new AppointmentReminderDeliveryRequest
            {
                AppointmentId = appointment.Id,
                ReminderId = reminder.Id,
                ReminderType = reminder.ReminderType,
                AppointmentDateUtc = appointment.AppointmentDateUtc,
                AppointmentLocalDisplay = local.ToString("yyyy-MM-dd HH:mm zzz"),
                ClinicCode = clinic.Slug,
                TimeZoneId = clinic.TimeZoneId,
            };

            await _sender.SendAsync(delivery, cancellationToken);

            reminder.Status = AppointmentReminderStatus.Sent;
            reminder.SentAtUtc = _timeProvider.GetUtcNow();
            reminder.LastError = null;
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Reminder sent. AppointmentId={AppointmentId} ReminderId={ReminderId} Type={ReminderType} AttemptCount={AttemptCount}",
                appointmentId,
                reminderId,
                reminder.ReminderType,
                reminder.AttemptCount);
        }
        catch (Exception ex)
        {
            reminder.Status = reminder.AttemptCount >= AppointmentReminder.MaxAttempts
                ? AppointmentReminderStatus.Failed
                : AppointmentReminderStatus.Pending;
            reminder.LastError = SanitizeError(ex);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Reminder failed. AppointmentId={AppointmentId} ReminderId={ReminderId} AttemptCount={AttemptCount} ErrorCode={ErrorCode}",
                appointmentId,
                reminderId,
                reminder.AttemptCount,
                reminder.LastError);

            if (reminder.Status == AppointmentReminderStatus.Pending)
            {
                throw; // allow Hangfire retry for transient failures
            }
        }
    }

    private static bool IsDeliverable(Appointment appointment, AppointmentReminderType type)
    {
        if (appointment.Status is AppointmentStatus.Completed or AppointmentStatus.NoShow)
        {
            return false;
        }

        if (AppointmentStatusTransitions.IsCancelled(appointment.Status))
        {
            return type == AppointmentReminderType.Cancellation;
        }

        return true;
    }

    private static string SanitizeError(Exception ex)
    {
        var message = ex.GetType().Name;
        return message.Length <= 500 ? message : message[..500];
    }
}
