namespace HealthCare.Contracts.Identity;

public sealed class PatientRegisterRequest
{
    public string Email { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public string ConfirmPassword { get; init; } = string.Empty;

    public string FirstName { get; init; } = string.Empty;

    public string LastName { get; init; } = string.Empty;

    public DateOnly? DateOfBirth { get; init; }

    public string? PhoneNumber { get; init; }
}

public sealed class PatientRegisterResponse
{
    /// <summary>
    /// Generic message that does not reveal whether the email was newly registered.
    /// </summary>
    public string Message { get; init; } = string.Empty;

    public bool RequiresEmailConfirmation { get; init; } = true;
}

public sealed class ConfirmEmailRequest
{
    public string Email { get; init; } = string.Empty;

    public string Token { get; init; } = string.Empty;
}

public sealed class ConfirmEmailResponse
{
    public string Message { get; init; } = string.Empty;

    public bool EmailConfirmed { get; init; }
}

public sealed class ResendConfirmationRequest
{
    public string Email { get; init; } = string.Empty;
}

public sealed class ResendConfirmationResponse
{
    public string Message { get; init; } = string.Empty;
}
