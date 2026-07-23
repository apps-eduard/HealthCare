namespace HealthCare.Contracts.Patients;

public static class PatientErrorCodes
{
    public const string UserNotFound = "patient.user_not_found";
    public const string PatientNotFound = "patient.not_found";
    public const string UserNotPatientRole = "patient.user_not_patient_role";
    public const string UserIsStaff = "patient.user_is_staff";
    public const string DuplicateUserLinkage = "patient.duplicate_user_linkage";
    public const string PatientAlreadyLinked = "patient.already_linked";
    public const string UserInactive = "patient.user_inactive";
    public const string NotFoundOrDenied = "patient.not_found_or_denied";
}
