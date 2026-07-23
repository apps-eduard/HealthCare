using HealthCare.Contracts.Patients;

namespace HealthCare.Application.Patients;

public sealed class PatientLinkageException : Exception
{
    public PatientLinkageException(string errorCode, string title)
        : base(title)
    {
        ErrorCode = errorCode;
        Title = title;
    }

    public string ErrorCode { get; }

    public string Title { get; }

    public static PatientLinkageException UserNotFound() =>
        new(PatientErrorCodes.UserNotFound, "The user account was not found.");

    public static PatientLinkageException PatientNotFound() =>
        new(PatientErrorCodes.PatientNotFound, "The patient record was not found.");

    public static PatientLinkageException UserNotPatientRole() =>
        new(PatientErrorCodes.UserNotPatientRole, "Only PATIENT accounts can be linked to a patient profile.");

    public static PatientLinkageException UserIsStaff() =>
        new(PatientErrorCodes.UserIsStaff, "Staff accounts cannot be linked as patient accounts.");

    public static PatientLinkageException DuplicateUserLinkage() =>
        new(PatientErrorCodes.DuplicateUserLinkage, "This user account is already linked to a patient profile.");

    public static PatientLinkageException PatientAlreadyLinked() =>
        new(PatientErrorCodes.PatientAlreadyLinked, "This patient profile is already linked to a user account.");

    public static PatientLinkageException UserInactive() =>
        new(PatientErrorCodes.UserInactive, "The user account is inactive.");
}
