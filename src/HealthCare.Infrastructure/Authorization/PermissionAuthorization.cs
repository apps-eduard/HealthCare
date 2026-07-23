using HealthCare.Application.Authorization;
using HealthCare.Contracts.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace HealthCare.Infrastructure.Authorization;

public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public PermissionRequirement(string permission)
    {
        Permission = permission;
    }

    public string Permission { get; }
}

public sealed class AnyPermissionRequirement : IAuthorizationRequirement
{
    public AnyPermissionRequirement(IReadOnlyList<string> permissions)
    {
        Permissions = permissions;
    }

    public IReadOnlyList<string> Permissions { get; }
}

public sealed class UnknownPermissionRequirement : IAuthorizationRequirement
{
    public UnknownPermissionRequirement(string permission)
    {
        Permission = permission;
    }

    public string Permission { get; }
}

/// <summary>
/// Builds permission policies dynamically from <c>perm:</c> policy names. Unknown permissions fail closed.
/// </summary>
public sealed class PermissionAuthorizationPolicyProvider : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback;

    public PermissionAuthorizationPolicyProvider(IOptions<AuthorizationOptions> options)
    {
        _fallback = new DefaultAuthorizationPolicyProvider(options);
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(Permissions.PolicyPrefix + "any:", StringComparison.Ordinal))
        {
            var raw = policyName[(Permissions.PolicyPrefix + "any:").Length..];
            var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0 || parts.Any(p => !Permissions.IsKnown(p)))
            {
                var unknown = new AuthorizationPolicyBuilder()
                    .AddRequirements(new UnknownPermissionRequirement(raw))
                    .Build();
                return Task.FromResult<AuthorizationPolicy?>(unknown);
            }

            var anyPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new AnyPermissionRequirement(parts))
                .Build();
            return Task.FromResult<AuthorizationPolicy?>(anyPolicy);
        }

        if (Permissions.TryParsePolicyName(policyName, out var permission))
        {
            if (!Permissions.IsKnown(permission))
            {
                var unknown = new AuthorizationPolicyBuilder()
                    .AddRequirements(new UnknownPermissionRequirement(permission))
                    .Build();
                return Task.FromResult<AuthorizationPolicy?>(unknown);
            }

            var policy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(permission))
                .Build();
            return Task.FromResult<AuthorizationPolicy?>(policy);
        }

        return _fallback.GetPolicyAsync(policyName);
    }
}

public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly IPermissionService _permissions;
    private readonly IAuthorizationAuditLogger _audit;

    public PermissionAuthorizationHandler(IPermissionService permissions, IAuthorizationAuditLogger audit)
    {
        _permissions = permissions;
        _audit = audit;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (_permissions.HasPermission(requirement.Permission))
        {
            context.Succeed(requirement);
        }
        else
        {
            _audit.PermissionDenied(
                requirement.Permission,
                "policy_permission_check",
                AuthorizationErrorCodes.PermissionDenied);
        }

        return Task.CompletedTask;
    }
}

public sealed class AnyPermissionAuthorizationHandler : AuthorizationHandler<AnyPermissionRequirement>
{
    private readonly IPermissionService _permissions;
    private readonly IAuthorizationAuditLogger _audit;

    public AnyPermissionAuthorizationHandler(IPermissionService permissions, IAuthorizationAuditLogger audit)
    {
        _permissions = permissions;
        _audit = audit;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AnyPermissionRequirement requirement)
    {
        if (_permissions.HasAnyPermission(requirement.Permissions.ToArray()))
        {
            context.Succeed(requirement);
        }
        else
        {
            _audit.PermissionDenied(
                string.Join('|', requirement.Permissions),
                "policy_any_permission_check",
                AuthorizationErrorCodes.PermissionDenied);
        }

        return Task.CompletedTask;
    }
}

public sealed class UnknownPermissionAuthorizationHandler : AuthorizationHandler<UnknownPermissionRequirement>
{
    private readonly IAuthorizationAuditLogger _audit;

    public UnknownPermissionAuthorizationHandler(IAuthorizationAuditLogger audit)
    {
        _audit = audit;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        UnknownPermissionRequirement requirement)
    {
        _audit.UnknownPermissionRequested(requirement.Permission);
        // Fail closed — never succeed.
        return Task.CompletedTask;
    }
}
