using HealthCare.Application.Appointments;
using HealthCare.Contracts.Appointments;
using Microsoft.Extensions.Logging;

namespace HealthCare.Infrastructure.Appointments;

/// <summary>
/// Development-only sender: logs operational counts without patient PII or clinical content.
/// </summary>
public sealed class DevelopmentClinicAppointmentSummarySender : IClinicAppointmentSummarySender
{
    private readonly ILogger<DevelopmentClinicAppointmentSummarySender> _logger;

    public DevelopmentClinicAppointmentSummarySender(ILogger<DevelopmentClinicAppointmentSummarySender> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(ClinicAppointmentSummaryResponse summary, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Clinic appointment summary delivered (Development). ClinicId={ClinicId} OrganizationId={OrganizationId} ClinicCode={ClinicCode} SummaryDate={SummaryDate} Total={Total} Requested={Requested} Confirmed={Confirmed} CheckedIn={CheckedIn} InProgress={InProgress} Completed={Completed} NoShow={NoShow} CancelledByPatient={CancelledByPatient} CancelledByClinic={CancelledByClinic} Doctors={DoctorGroups}",
            summary.ClinicId,
            summary.OrganizationId,
            summary.ClinicCode,
            summary.SummaryDate,
            summary.TotalAppointments,
            summary.Requested,
            summary.Confirmed,
            summary.CheckedIn,
            summary.InProgress,
            summary.Completed,
            summary.NoShow,
            summary.CancelledByPatient,
            summary.CancelledByClinic,
            summary.ByDoctor.Count);

        return Task.CompletedTask;
    }
}
