using HealthCare.Contracts.Organizations;

namespace HealthCare.Application.Organizations;

public sealed class OrganizationDirectoryException : Exception
{
    public OrganizationDirectoryException(string errorCode, string title, int statusCode = 403)
        : base(title)
    {
        ErrorCode = errorCode;
        Title = title;
        StatusCode = statusCode;
    }

    public string ErrorCode { get; }

    public string Title { get; }

    public int StatusCode { get; }

    public static OrganizationDirectoryException NotFound() =>
        new(OrganizationErrorCodes.NotFound, "Organization was not found.", 404);

    public static OrganizationDirectoryException DirectoryAccessDenied() =>
        new(OrganizationErrorCodes.DirectoryAccessDenied, "Organization directory access is denied.", 403);
}
