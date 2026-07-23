namespace HealthCare.Domain.Patients;

/// <summary>
/// Per-clinic monotonic counter used to allocate <see cref="ClinicPatient.LocalPatientNumber"/> values.
/// </summary>
public sealed class ClinicPatientNumberSequence
{
    public Guid ClinicId { get; set; }

    public long LastValue { get; set; }
}
