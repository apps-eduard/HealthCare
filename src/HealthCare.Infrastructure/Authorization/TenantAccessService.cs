using HealthCare.Application.Authorization;
using HealthCare.Domain.Identity;
using Microsoft.Extensions.Logging;

namespace HealthCare.Infrastructure.Authorization;

public sealed class TenantAccessService : ITenantAccessService
{
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentStaff _currentStaff;
    private readonly ICurrentPatient _currentPatient;
    private readonly IAuthorizationAuditLogger _audit;
    private readonly ILogger<TenantAccessService> _logger;

    public TenantAccessService(
        ICurrentUser currentUser,
        ICurrentStaff currentStaff,
        ICurrentPatient currentPatient,
        IAuthorizationAuditLogger audit,
        ILogger<TenantAccessService> logger)
    {
        _currentUser = currentUser;
        _currentStaff = currentStaff;
        _currentPatient = currentPatient;
        _audit = audit;
        _logger = logger;
    }

    public bool CanAccessOrganization(Guid organizationId, PlatformAdminBypass bypass = PlatformAdminBypass.None)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is null)
        {
            return false;
        }

        if (bypass == PlatformAdminBypass.Explicit && _currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            _audit.ExplicitPlatformBypassUsed("organization_access", organizationId, null);
            return true;
        }

        if (!_currentStaff.HasActiveMembership)
        {
            return false;
        }

        return _currentStaff.OrganizationId == organizationId;
    }

    public bool CanAccessClinic(Guid clinicId, PlatformAdminBypass bypass = PlatformAdminBypass.None)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is null)
        {
            return false;
        }

        if (bypass == PlatformAdminBypass.Explicit && _currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            _audit.ExplicitPlatformBypassUsed("clinic_access", null, clinicId);
            return true;
        }

        if (!_currentStaff.HasActiveMembership)
        {
            return false;
        }

        return _currentStaff.ClinicId == clinicId;
    }

    public bool CanAccessPatient(Guid patientId, PlatformAdminBypass bypass = PlatformAdminBypass.None)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is null)
        {
            return false;
        }

        if (bypass == PlatformAdminBypass.Explicit && _currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            _audit.ExplicitPlatformBypassUsed("patient_access", null, null);
            return true;
        }

        if (!_currentPatient.HasLinkedPatient || _currentPatient.PatientId is null)
        {
            return false;
        }

        return _currentPatient.PatientId.Value == patientId;
    }

    public void EnsureCanAccessOrganization(Guid organizationId, PlatformAdminBypass bypass = PlatformAdminBypass.None)
    {
        if (!_currentUser.IsAuthenticated)
        {
            throw AuthorizationException.NotAuthenticated();
        }

        if (!CanAccessOrganization(organizationId, bypass))
        {
            LogDenial("organization_access_denied", organizationId.ToString());
            throw AuthorizationException.OrganizationAccessDenied();
        }
    }

    public void EnsureCanAccessClinic(Guid clinicId, PlatformAdminBypass bypass = PlatformAdminBypass.None)
    {
        if (!_currentUser.IsAuthenticated)
        {
            throw AuthorizationException.NotAuthenticated();
        }

        if (!CanAccessClinic(clinicId, bypass))
        {
            LogDenial("clinic_access_denied", clinicId.ToString());
            throw AuthorizationException.ClinicAccessDenied();
        }
    }

    public void EnsureCanAccessPatient(Guid patientId, PlatformAdminBypass bypass = PlatformAdminBypass.None)
    {
        if (!_currentUser.IsAuthenticated)
        {
            throw AuthorizationException.NotAuthenticated();
        }

        if (bypass == PlatformAdminBypass.Explicit && _currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            if (!CanAccessPatient(patientId, bypass))
            {
                LogDenial("patient_self_scope_denied", patientId.ToString());
                throw AuthorizationException.PatientSelfScopeDenied();
            }

            return;
        }

        if (_currentUser.IsInRole(AppRoles.Patient) && !_currentPatient.HasLinkedPatient)
        {
            LogDenial("missing_patient_linkage", patientId.ToString());
            throw AuthorizationException.MissingPatientLinkage();
        }

        if (!CanAccessPatient(patientId, bypass))
        {
            LogDenial("patient_self_scope_denied", patientId.ToString());
            throw AuthorizationException.PatientSelfScopeDenied();
        }
    }

    private void LogDenial(string reasonCode, string resourceKey)
    {
        _logger.LogInformation(
            "Authorization denied. UserId={UserId} Reason={ReasonCode} Resource={ResourceKey}",
            _currentUser.UserId,
            reasonCode,
            resourceKey);
    }
}
