namespace HealthCare.Domain.Identity;

/// <summary>
/// ASP.NET Core Identity role name constants. Use these instead of duplicated string literals.
/// </summary>
public static class AppRoles
{
    public const string PlatformAdmin = "PLATFORM_ADMIN";
    public const string OrganizationAdmin = "ORGANIZATION_ADMIN";
    public const string ClinicAdmin = "CLINIC_ADMIN";
    public const string Doctor = "DOCTOR";
    public const string Nurse = "NURSE";
    public const string Receptionist = "RECEPTIONIST";
    public const string Patient = "PATIENT";

    public static IReadOnlyList<string> All { get; } =
    [
        PlatformAdmin,
        OrganizationAdmin,
        ClinicAdmin,
        Doctor,
        Nurse,
        Receptionist,
        Patient,
    ];
}
