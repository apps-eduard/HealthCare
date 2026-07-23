namespace HealthCare.Application.Authorization;

/// <summary>
/// Patient self-scope foundation. PatientId is null until a Patient record is linked server-side.
/// </summary>
public interface ICurrentPatient
{
    bool HasLinkedPatient { get; }

    Guid? PatientId { get; }
}
