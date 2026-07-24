namespace HealthCare.Contracts.Identity;

public sealed class CompletePasswordResetRequest
{
    public required string Email { get; init; }

    public required string Token { get; init; }

    public required string NewPassword { get; init; }
}

public sealed class CompletePasswordResetResponse
{
    public required string Message { get; init; }
}
