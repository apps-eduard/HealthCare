namespace HealthCare.Contracts.Identity;

public sealed class CurrentUserPermissionsResponse
{
    public required IReadOnlyList<string> Permissions { get; init; }
}
