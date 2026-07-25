using HealthCare.Contracts.Patients;
using HealthCare.Mobile.Core.Api;

namespace HealthCare.Mobile.Core.Patients;

/// <summary>Pure helpers for profile edit concurrency UX (unit-testable).</summary>
public static class ProfileConcurrencyUx
{
    public static bool IsConcurrencyConflict(ApiProblem? problem) =>
        problem is not null
        && string.Equals(problem.ErrorCode, PatientErrorCodes.ConcurrencyConflict, StringComparison.Ordinal);

    public static string ConflictUserMessage =>
        "Your profile was updated elsewhere. Reload the latest profile before saving again.";
}
