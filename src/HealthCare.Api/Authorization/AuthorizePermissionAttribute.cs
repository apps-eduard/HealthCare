using HealthCare.Application.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace HealthCare.Api.Authorization;

/// <summary>
/// Declares a dynamic permission policy (<c>perm:{name}</c>).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class AuthorizePermissionAttribute : AuthorizeAttribute
{
    public AuthorizePermissionAttribute(string permission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        Permission = permission;
        Policy = Permissions.ToPolicyName(permission);
    }

    public string Permission { get; }
}

/// <summary>
/// Succeeds when the caller has any of the listed permissions.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class AuthorizeAnyPermissionAttribute : AuthorizeAttribute
{
    public AuthorizeAnyPermissionAttribute(params string[] permissions)
    {
        if (permissions is null || permissions.Length == 0)
        {
            throw new ArgumentException("At least one permission is required.", nameof(permissions));
        }

        PermissionsList = permissions;
        Policy = Permissions.PolicyPrefix + "any:" + string.Join(',', permissions);
    }

    public IReadOnlyList<string> PermissionsList { get; }
}
