namespace HealthCare.Application.Authorization;

/// <summary>
/// Explicit PLATFORM_ADMIN bypass must be opted into by calling code — never implied by missing tenant context.
/// </summary>
public enum PlatformAdminBypass
{
    None = 0,
    Explicit = 1,
}

public interface ITenantAccessService
{
    bool CanAccessOrganization(Guid organizationId, PlatformAdminBypass bypass = PlatformAdminBypass.None);

    bool CanAccessClinic(Guid clinicId, PlatformAdminBypass bypass = PlatformAdminBypass.None);

    bool CanAccessPatient(Guid patientId, PlatformAdminBypass bypass = PlatformAdminBypass.None);

    void EnsureCanAccessOrganization(Guid organizationId, PlatformAdminBypass bypass = PlatformAdminBypass.None);

    void EnsureCanAccessClinic(Guid clinicId, PlatformAdminBypass bypass = PlatformAdminBypass.None);

    void EnsureCanAccessPatient(Guid patientId, PlatformAdminBypass bypass = PlatformAdminBypass.None);
}
