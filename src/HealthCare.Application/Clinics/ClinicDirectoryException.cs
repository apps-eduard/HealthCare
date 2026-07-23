using HealthCare.Contracts.Clinics;

namespace HealthCare.Application.Clinics;

public sealed class ClinicDirectoryException : Exception
{
    public ClinicDirectoryException(string errorCode, string title, int statusCode = 403)
        : base(title)
    {
        ErrorCode = errorCode;
        Title = title;
        StatusCode = statusCode;
    }

    public string ErrorCode { get; }

    public string Title { get; }

    public int StatusCode { get; }

    public static ClinicDirectoryException NotFound() =>
        new(ClinicErrorCodes.NotFound, "Clinic was not found.", 404);

    public static ClinicDirectoryException DirectoryAccessDenied() =>
        new(ClinicErrorCodes.DirectoryAccessDenied, "Clinic directory access is denied.", 403);

    public static ClinicDirectoryException InvalidScope() =>
        new(ClinicErrorCodes.InvalidScope, "The requested clinic scope is invalid.", 400);

    public static ClinicDirectoryException OrganizationScopeRequired() =>
        new(ClinicErrorCodes.OrganizationScopeRequired, "An organization scope is required.", 400);
}
