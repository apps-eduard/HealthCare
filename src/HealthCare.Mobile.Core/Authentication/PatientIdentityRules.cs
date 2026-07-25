using HealthCare.Contracts.Identity;

namespace HealthCare.Mobile.Core.Authentication;

/// <summary>
/// Server-validated Patient account rules for the mobile client.
/// Do not trust JWT claims alone — use <see cref="CurrentUserResponse"/> from <c>/auth/me</c>.
/// </summary>
public static class PatientIdentityRules
{
    public const string PatientRole = "PATIENT";

    public static bool HasPatientRole(CurrentUserResponse user) =>
        user.Roles.Any(r => string.Equals(r, PatientRole, StringComparison.OrdinalIgnoreCase));

    public static bool HasValidPatientLinkage(CurrentUserResponse user) =>
        user.HasLinkedPatient
        && user.PatientId is not null
        && user.PatientId != Guid.Empty;

    /// <summary>
    /// Patient mobile access requires PATIENT role, linked patient, and no active staff membership.
    /// </summary>
    public static bool IsEligiblePatientAccount(CurrentUserResponse user) =>
        HasPatientRole(user)
        && HasValidPatientLinkage(user)
        && !user.HasActiveStaffMembership;

    public static string LinkageFailureMessage =>
        "This account is not linked to a Patient profile and cannot use the Patient app.";
}
