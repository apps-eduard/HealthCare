using HealthCare.Application.Authorization;
using HealthCare.Contracts.Patients;

namespace HealthCare.Application.Patients;

public interface IClinicEnrollmentService
{
    Task<ClinicPatientEnrollmentResponse> EnrollAsync(
        Guid clinicId,
        Guid patientId,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);
}

public interface ILocalPatientNumberGenerator
{
    /// <summary>
    /// Allocates the next clinic-local number in format P-000001. Concurrent-safe.
    /// </summary>
    Task<string> AllocateNextAsync(Guid clinicId, CancellationToken cancellationToken = default);
}
