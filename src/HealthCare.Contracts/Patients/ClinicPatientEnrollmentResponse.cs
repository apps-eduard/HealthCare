namespace HealthCare.Contracts.Patients;

public sealed class ClinicPatientEnrollmentResponse
{
    public Guid ClinicPatientId { get; init; }

    public Guid ClinicId { get; init; }

    public Guid PatientId { get; init; }

    public string LocalPatientNumber { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public bool AlreadyEnrolled { get; init; }
}
