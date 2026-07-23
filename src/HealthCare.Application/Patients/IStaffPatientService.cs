using HealthCare.Application.Authorization;
using HealthCare.Contracts.Common;
using HealthCare.Contracts.Patients;

namespace HealthCare.Application.Patients;

public interface IStaffPatientService
{
    Task<PagedResponse<StaffPatientSummaryResponse>> SearchAsync(
        StaffPatientSearchRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task<StaffPatientDetailResponse> GetByPatientIdAsync(
        Guid patientId,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task<StaffPatientDetailResponse> UpdateClinicProfileAsync(
        Guid patientId,
        UpdateClinicPatientRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);
}

public sealed class ClinicPatientConcurrencyException : Exception
{
    public ClinicPatientConcurrencyException()
        : base("The clinic patient record was modified by another request. Reload and retry.")
    {
        ErrorCode = PatientErrorCodes.ClinicPatientConcurrencyConflict;
        Title = "The clinic patient record was modified by another request. Reload and retry.";
        StatusCode = 409;
    }

    public string ErrorCode { get; }

    public string Title { get; }

    public int StatusCode { get; }
}
