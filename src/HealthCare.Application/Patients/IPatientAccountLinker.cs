namespace HealthCare.Application.Patients;

public interface IPatientAccountLinker
{
    /// <summary>
    /// Links a PATIENT ApplicationUser to a Patient record using server-side validation only.
    /// </summary>
    Task LinkUserToPatientAsync(Guid userId, Guid patientId, CancellationToken cancellationToken = default);
}
