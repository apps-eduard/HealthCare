namespace HealthCare.Infrastructure.Patients;

public sealed class DevelopmentPatientOptions
{
    public const string SectionName = "DevelopmentSeed:Patient";

    public string? Email { get; set; }

    public string? Password { get; set; }

    public string FirstName { get; set; } = "Dev";

    public string LastName { get; set; } = "Patient";

    public string OrganizationName { get; set; } = "Dev Health Organization";

    public string OrganizationSlug { get; set; } = "dev-health-org";

    public string ClinicName { get; set; } = "Dev Clinic A";

    public string ClinicSlug { get; set; } = "dev-clinic-a";

    public string LocalPatientNumber { get; set; } = "DEV-P-0001";

    public string? StaffEmail { get; set; }

    public string? StaffPassword { get; set; }

    public string? OtherClinicStaffEmail { get; set; }

    public string? OtherClinicStaffPassword { get; set; }

    public string OtherClinicName { get; set; } = "Dev Clinic B";

    public string OtherClinicSlug { get; set; } = "dev-clinic-b";
}
