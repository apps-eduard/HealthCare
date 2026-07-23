using HealthCare.Domain.Clinics;

namespace HealthCare.Domain.Patients;

/// <summary>
/// Clinic-owned relationship between a global patient and one clinic.
/// Local medical-record / patient numbers live here, not on the global Patient.
/// </summary>
public sealed class ClinicPatient
{
    public Guid Id { get; set; }

    public Guid ClinicId { get; set; }

    public Guid PatientId { get; set; }

    public required string LocalPatientNumber { get; set; }

    public ClinicPatientStatus Status { get; set; } = ClinicPatientStatus.Active;

    public DateTimeOffset RegisteredAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public Clinic? Clinic { get; set; }

    public Patient? Patient { get; set; }
}
