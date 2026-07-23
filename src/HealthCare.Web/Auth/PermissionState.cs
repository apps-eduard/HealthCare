using HealthCare.Contracts.Identity;

namespace HealthCare.Web.Auth;

public interface IPermissionState
{
    bool IsReady { get; }

    CurrentUserResponse? CurrentUser { get; }

    IReadOnlySet<string> Permissions { get; }

    bool Has(string permission);

    bool IsStaffUser { get; }

    bool IsPatientOnly { get; }

    bool CanFilterByClinic { get; }

    Task SetFromUserAsync(CurrentUserResponse? user, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);

    event Action? Changed;
}

public sealed class PermissionState : IPermissionState
{
    private HashSet<string> _permissions = new(StringComparer.Ordinal);

    public bool IsReady { get; private set; }

    public CurrentUserResponse? CurrentUser { get; private set; }

    public IReadOnlySet<string> Permissions => _permissions;

    public bool Has(string permission) =>
        !string.IsNullOrWhiteSpace(permission) && _permissions.Contains(permission);

    public bool IsStaffUser =>
        CurrentUser is { HasActiveStaffMembership: true }
        || (CurrentUser?.Roles.Contains(WebRoles.PlatformAdmin, StringComparer.Ordinal) ?? false);

    public bool IsPatientOnly =>
        CurrentUser is not null
        && CurrentUser.Roles.Contains(WebRoles.Patient, StringComparer.Ordinal)
        && !CurrentUser.HasActiveStaffMembership
        && !CurrentUser.Roles.Contains(WebRoles.PlatformAdmin, StringComparer.Ordinal);

    public bool CanFilterByClinic =>
        CurrentUser is not null
        && (CurrentUser.Roles.Contains(WebRoles.OrganizationAdmin, StringComparer.Ordinal)
            || CurrentUser.Roles.Contains(WebRoles.PlatformAdmin, StringComparer.Ordinal));

    public event Action? Changed;

    public Task SetFromUserAsync(CurrentUserResponse? user, CancellationToken cancellationToken = default)
    {
        CurrentUser = user;
        _permissions = user?.Permissions is { Count: > 0 } perms
            ? new HashSet<string>(perms, StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        IsReady = true;
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        CurrentUser = null;
        _permissions = new HashSet<string>(StringComparer.Ordinal);
        IsReady = true;
        Changed?.Invoke();
        return Task.CompletedTask;
    }
}
