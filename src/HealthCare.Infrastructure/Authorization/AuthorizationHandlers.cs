using HealthCare.Application.Authorization;
using HealthCare.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;

namespace HealthCare.Infrastructure.Authorization;

public sealed class StaffUserRequirement : IAuthorizationRequirement;

public sealed class OrganizationScopedRequirement : IAuthorizationRequirement;

public sealed class ClinicScopedRequirement : IAuthorizationRequirement;

public sealed class PatientUserRequirement : IAuthorizationRequirement;

public sealed class PatientSelfScopeRequirement : IAuthorizationRequirement;

public sealed class StaffUserHandler : AuthorizationHandler<StaffUserRequirement>
{
    private readonly ICurrentStaff _currentStaff;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<StaffUserHandler> _logger;

    public StaffUserHandler(ICurrentStaff currentStaff, ICurrentUser currentUser, ILogger<StaffUserHandler> logger)
    {
        _currentStaff = currentStaff;
        _currentUser = currentUser;
        _logger = logger;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        StaffUserRequirement requirement)
    {
        if (_currentStaff.HasActiveMembership)
        {
            context.Succeed(requirement);
        }
        else
        {
            _logger.LogInformation(
                "Authorization denied. UserId={UserId} Reason={ReasonCode} Operation={Operation}",
                _currentUser.UserId,
                "missing_staff_membership",
                nameof(StaffUserRequirement));
        }

        return Task.CompletedTask;
    }
}

public sealed class OrganizationScopedHandler : AuthorizationHandler<OrganizationScopedRequirement>
{
    private readonly ICurrentStaff _currentStaff;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<OrganizationScopedHandler> _logger;

    public OrganizationScopedHandler(
        ICurrentStaff currentStaff,
        ICurrentUser currentUser,
        ILogger<OrganizationScopedHandler> logger)
    {
        _currentStaff = currentStaff;
        _currentUser = currentUser;
        _logger = logger;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OrganizationScopedRequirement requirement)
    {
        if (_currentStaff.HasActiveMembership && _currentStaff.OrganizationId != Guid.Empty)
        {
            context.Succeed(requirement);
        }
        else
        {
            _logger.LogInformation(
                "Authorization denied. UserId={UserId} Reason={ReasonCode} Operation={Operation}",
                _currentUser.UserId,
                "organization_scope_missing",
                nameof(OrganizationScopedRequirement));
        }

        return Task.CompletedTask;
    }
}

public sealed class ClinicScopedHandler : AuthorizationHandler<ClinicScopedRequirement>
{
    private readonly ICurrentStaff _currentStaff;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<ClinicScopedHandler> _logger;

    public ClinicScopedHandler(ICurrentStaff currentStaff, ICurrentUser currentUser, ILogger<ClinicScopedHandler> logger)
    {
        _currentStaff = currentStaff;
        _currentUser = currentUser;
        _logger = logger;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ClinicScopedRequirement requirement)
    {
        if (_currentStaff.HasActiveMembership && _currentStaff.ClinicId != Guid.Empty)
        {
            context.Succeed(requirement);
        }
        else
        {
            _logger.LogInformation(
                "Authorization denied. UserId={UserId} Reason={ReasonCode} Operation={Operation}",
                _currentUser.UserId,
                "clinic_scope_missing",
                nameof(ClinicScopedRequirement));
        }

        return Task.CompletedTask;
    }
}

public sealed class PatientUserHandler : AuthorizationHandler<PatientUserRequirement>
{
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<PatientUserHandler> _logger;

    public PatientUserHandler(ICurrentUser currentUser, ILogger<PatientUserHandler> logger)
    {
        _currentUser = currentUser;
        _logger = logger;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PatientUserRequirement requirement)
    {
        if (_currentUser.IsInRole(AppRoles.Patient))
        {
            context.Succeed(requirement);
        }
        else
        {
            _logger.LogInformation(
                "Authorization denied. UserId={UserId} Reason={ReasonCode} Operation={Operation}",
                _currentUser.UserId,
                "patient_role_required",
                nameof(PatientUserRequirement));
        }

        return Task.CompletedTask;
    }
}

public sealed class PatientSelfScopeHandler : AuthorizationHandler<PatientSelfScopeRequirement>
{
    private readonly ICurrentPatient _currentPatient;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<PatientSelfScopeHandler> _logger;

    public PatientSelfScopeHandler(
        ICurrentPatient currentPatient,
        ICurrentUser currentUser,
        ILogger<PatientSelfScopeHandler> logger)
    {
        _currentPatient = currentPatient;
        _currentUser = currentUser;
        _logger = logger;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PatientSelfScopeRequirement requirement)
    {
        if (_currentUser.IsInRole(AppRoles.Patient) && _currentPatient.HasLinkedPatient)
        {
            context.Succeed(requirement);
        }
        else
        {
            _logger.LogInformation(
                "Authorization denied. UserId={UserId} Reason={ReasonCode} Operation={Operation}",
                _currentUser.UserId,
                "missing_patient_linkage",
                nameof(PatientSelfScopeRequirement));
        }

        return Task.CompletedTask;
    }
}
