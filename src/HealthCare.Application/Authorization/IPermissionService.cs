namespace HealthCare.Application.Authorization;

/// <summary>
/// Resolves permissions from trusted server-side roles and membership — never from client-supplied claims alone.
/// </summary>
public interface IPermissionService
{
    bool HasPermission(string permission);

    bool HasAnyPermission(params string[] permissions);

    void RequirePermission(string permission);

    IReadOnlyList<string> GetCurrentPermissions();

    IReadOnlyList<string> GetEffectiveRoles();
}
