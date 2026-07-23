using HealthCare.Contracts.Identity;

namespace HealthCare.Application.Identity;

public interface IPatientRegistrationService
{
    Task<PatientRegisterResponse> RegisterAsync(
        PatientRegisterRequest request,
        CancellationToken cancellationToken = default);

    Task<ConfirmEmailResponse> ConfirmEmailAsync(
        ConfirmEmailRequest request,
        CancellationToken cancellationToken = default);

    Task<ResendConfirmationResponse> ResendConfirmationAsync(
        ResendConfirmationRequest request,
        CancellationToken cancellationToken = default);
}
