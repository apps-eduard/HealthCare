using HealthCare.Application.Authorization;
using HealthCare.Application.Identity;
using HealthCare.Application.Patients;
using HealthCare.Contracts.Identity;
using HealthCare.Contracts.Patients;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthCare.Api.Controllers;

public sealed class AuthController : ApiControllerBase
{
    private readonly IAuthService _authService;
    private readonly IPatientRegistrationService _patientRegistration;
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentStaff _currentStaff;
    private readonly ICurrentPatient _currentPatient;
    private readonly IDevelopmentConfirmationTokenStore _confirmationTokenStore;
    private readonly IHostEnvironment _environment;

    public AuthController(
        IAuthService authService,
        IPatientRegistrationService patientRegistration,
        ICurrentUser currentUser,
        ICurrentStaff currentStaff,
        ICurrentPatient currentPatient,
        IDevelopmentConfirmationTokenStore confirmationTokenStore,
        IHostEnvironment environment)
    {
        _authService = authService;
        _patientRegistration = patientRegistration;
        _currentUser = currentUser;
        _currentStaff = currentStaff;
        _currentPatient = currentPatient;
        _confirmationTokenStore = confirmationTokenStore;
        _environment = environment;
    }

    [AllowAnonymous]
    [HttpPost("register/patient")]
    [ProducesResponseType(typeof(PatientRegisterResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PatientRegisterResponse>> RegisterPatient(
        [FromBody] PatientRegisterRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _patientRegistration.RegisterAsync(request, cancellationToken);
        return Ok(result);
    }

    [AllowAnonymous]
    [HttpPost("confirm-email")]
    [ProducesResponseType(typeof(ConfirmEmailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ConfirmEmailResponse>> ConfirmEmail(
        [FromBody] ConfirmEmailRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _patientRegistration.ConfirmEmailAsync(request, cancellationToken);
        return Ok(result);
    }

    [AllowAnonymous]
    [HttpPost("resend-confirmation")]
    [ProducesResponseType(typeof(ResendConfirmationResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ResendConfirmationResponse>> ResendConfirmation(
        [FromBody] ResendConfirmationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _patientRegistration.ResendConfirmationAsync(request, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Development-only helper to retrieve the last captured confirmation token for manual testing.
    /// Never enabled outside Development.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("dev/confirmation-token")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetDevelopmentConfirmationToken([FromQuery] string email)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(email) || !_confirmationTokenStore.TryGet(email, out var token))
        {
            return NotFound();
        }

        return Ok(new { email, token });
    }

    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AuthTokenResponse>> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var client = CreateClientContext();
        var result = await _authService.LoginAsync(request, client, cancellationToken);
        return Ok(result);
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(AuthTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthTokenResponse>> Refresh(
        [FromBody] RefreshTokenRequest request,
        CancellationToken cancellationToken)
    {
        var client = CreateClientContext();
        var result = await _authService.RefreshAsync(request, client, cancellationToken);
        return Ok(result);
    }

    [AllowAnonymous]
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(
        [FromBody] LogoutRequest request,
        CancellationToken cancellationToken)
    {
        await _authService.LogoutAsync(request, cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = AuthorizationPolicies.Authenticated)]
    [HttpGet("me")]
    [ProducesResponseType(typeof(CurrentUserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public ActionResult<CurrentUserResponse> Me()
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is null)
        {
            throw AuthorizationException.NotAuthenticated();
        }

        return Ok(new CurrentUserResponse
        {
            UserId = _currentUser.UserId.Value,
            Email = _currentUser.Email,
            Roles = _currentUser.Roles,
            OrganizationId = _currentUser.OrganizationId,
            ClinicId = _currentUser.ClinicId,
            PatientId = _currentUser.PatientId,
            StaffMemberId = _currentUser.StaffMemberId,
            HasActiveStaffMembership = _currentStaff.HasActiveMembership,
            HasLinkedPatient = _currentPatient.HasLinkedPatient,
        });
    }

    private AuthClientContext CreateClientContext()
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();
        return new AuthClientContext(ip, string.IsNullOrWhiteSpace(userAgent) ? null : userAgent);
    }
}
