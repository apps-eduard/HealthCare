using HealthCare.Contracts.Patients;
using HealthCare.Web.Services;

namespace HealthCare.Web.Patients;

/// <summary>
/// Builds server-side staff patient search queries (testable without bUnit).
/// </summary>
public static class StaffPatientSearchQueryBuilder
{
    public static StaffPatientSearchRequest Build(
        string? search,
        string? localPatientNumber,
        bool? patientIsActive,
        string? clinicPatientStatus,
        Guid? clinicId,
        int page,
        int pageSize,
        string sortBy,
        string sortDirection)
    {
        return new StaffPatientSearchRequest
        {
            Search = string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
            LocalPatientNumber = string.IsNullOrWhiteSpace(localPatientNumber) ? null : localPatientNumber.Trim(),
            PatientIsActive = patientIsActive,
            ClinicPatientStatus = string.IsNullOrWhiteSpace(clinicPatientStatus) ? null : clinicPatientStatus.Trim(),
            ClinicId = clinicId is Guid id && id != Guid.Empty ? id : null,
            Page = Math.Max(1, page),
            PageSize = Math.Clamp(pageSize, 1, 100),
            SortBy = string.IsNullOrWhiteSpace(sortBy) ? "registeredAtUtc" : sortBy.Trim(),
            SortDirection = string.Equals(sortDirection, "asc", StringComparison.OrdinalIgnoreCase) ? "asc" : "desc",
        };
    }
}
