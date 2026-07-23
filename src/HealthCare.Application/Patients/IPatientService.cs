using HealthCare.Application.Authorization;
using HealthCare.Contracts.Patients;

namespace HealthCare.Application.Patients;

public interface IPatientService
{
    Task<PatientProfileResponse> GetCurrentPatientProfileAsync(CancellationToken cancellationToken = default);

    Task<PatientProfileResponse> GetPatientByIdAsync(
        Guid patientId,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task EnsureCanAccessPatientRecordAsync(
        Guid patientId,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);
}
