namespace HealthCare.Application.Authorization;

/// <summary>
/// Named authorization policy constants. Use these instead of string literals.
/// </summary>
public static class AuthorizationPolicies
{
    public const string Authenticated = "Authenticated";
    public const string PlatformAdmin = "PlatformAdmin";
    public const string OrganizationScoped = "OrganizationScoped";
    public const string ClinicScoped = "ClinicScoped";
    public const string StaffUser = "StaffUser";
    public const string PatientUser = "PatientUser";
    public const string PatientSelfScope = "PatientSelfScope";
}
