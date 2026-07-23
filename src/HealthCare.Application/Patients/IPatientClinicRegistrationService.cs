using HealthCare.Contracts.Patients;
using HealthCare.Domain.Clinics;

namespace HealthCare.Application.Patients;

/// <summary>
/// Resolves a trusted public clinic reference (clinic code / slug) to a clinic entity.
/// </summary>
public interface IClinicPublicLookup
{
    Task<Clinic?> FindByPublicCodeAsync(string clinicCode, CancellationToken cancellationToken = default);
}

public interface IPatientClinicRegistrationService
{
    /// <summary>
    /// Registers the authenticated patient with a clinic identified by public clinic code.
    /// Idempotent: returns the existing ClinicPatient when already enrolled.
    /// </summary>
    Task<ClinicPatientEnrollmentResponse> RegisterCurrentPatientWithClinicAsync(
        RegisterPatientWithClinicRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class PatientConcurrencyException : Exception
{
    public PatientConcurrencyException()
        : base("The patient profile was modified by another request. Reload and retry.")
    {
        ErrorCode = PatientErrorCodes.ConcurrencyConflict;
        Title = "The patient profile was modified by another request. Reload and retry.";
        StatusCode = 409;
    }

    public string ErrorCode { get; }

    public string Title { get; }

    public int StatusCode { get; }
}

public sealed class PatientClinicRegistrationException : Exception
{
    public PatientClinicRegistrationException(string errorCode, string title, int statusCode = 404)
        : base(title)
    {
        ErrorCode = errorCode;
        Title = title;
        StatusCode = statusCode;
    }

    public string ErrorCode { get; }

    public string Title { get; }

    public int StatusCode { get; }

    public static PatientClinicRegistrationException InvalidClinicCode() =>
        new(PatientErrorCodes.ClinicCodeInvalid, "The clinic code is invalid.", 404);

    public static PatientClinicRegistrationException ClinicInactive() =>
        new(PatientErrorCodes.ClinicInactive, "The clinic is not available.", 404);

    public static PatientClinicRegistrationException OrganizationInactive() =>
        new(PatientErrorCodes.OrganizationInactive, "The clinic is not available.", 404);
}
