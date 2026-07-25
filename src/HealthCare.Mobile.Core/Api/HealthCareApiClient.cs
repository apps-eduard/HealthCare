using HealthCare.Contracts.Identity;
using HealthCare.Contracts.Patients;
using HealthCare.Mobile.Core.Authentication;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HealthCare.Mobile.Core.Api;

public interface IHealthCareApiClient
{
    Task<ApiResult<HealthStatusDto>> GetHealthAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<PatientRegisterResponse>> RegisterPatientAsync(
        PatientRegisterRequest request,
        CancellationToken cancellationToken = default);

    Task<ApiResult<ConfirmEmailResponse>> ConfirmEmailAsync(
        ConfirmEmailRequest request,
        CancellationToken cancellationToken = default);

    Task<ApiResult<ResendConfirmationResponse>> ResendConfirmationAsync(
        ResendConfirmationRequest request,
        CancellationToken cancellationToken = default);

    Task<ApiResult<AuthTokenResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<CurrentUserResponse>> GetMeAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<bool>> LogoutAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<PatientProfileResponse>> GetPatientProfileAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<PatientProfileResponse>> UpdatePatientProfileAsync(
        UpdatePatientProfileRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class HealthStatusDto
{
    public string? Status { get; init; }
}

public sealed class HealthCareApiClient : IHealthCareApiClient
{
    private static readonly JsonSerializerOptions PatchJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAuthSessionService _session;
    private readonly ILogger<HealthCareApiClient> _logger;

    public HealthCareApiClient(
        IHttpClientFactory httpClientFactory,
        IAuthSessionService session,
        ILogger<HealthCareApiClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _session = session;
        _logger = logger;
    }

    public async Task<ApiResult<HealthStatusDto>> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(MobileHttpClientNames.Anonymous);
            using var response = await client.GetAsync("health", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var raw = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogInformation("Health check failed. Status={StatusCode}", (int)response.StatusCode);
                return ApiResult<HealthStatusDto>.Failure(ApiProblemMapper.FromStatusCode(response.StatusCode, raw));
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase))
            {
                var value = await response.Content.ReadFromJsonAsync<HealthStatusDto>(cancellationToken: cancellationToken);
                return ApiResult<HealthStatusDto>.Success(value ?? new HealthStatusDto { Status = "Healthy" });
            }

            var text = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
            return ApiResult<HealthStatusDto>.Success(new HealthStatusDto
            {
                Status = string.IsNullOrWhiteSpace(text) ? "Healthy" : text,
            });
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "Health check exception.");
            return ApiResult<HealthStatusDto>.Failure(ApiProblemMapper.FromException(ex));
        }
    }

    public Task<ApiResult<PatientRegisterResponse>> RegisterPatientAsync(
        PatientRegisterRequest request,
        CancellationToken cancellationToken = default) =>
        SendAnonymousAsync<PatientRegisterResponse>(
            HttpMethod.Post,
            "api/v1/auth/register/patient",
            request,
            cancellationToken);

    public Task<ApiResult<ConfirmEmailResponse>> ConfirmEmailAsync(
        ConfirmEmailRequest request,
        CancellationToken cancellationToken = default) =>
        SendAnonymousAsync<ConfirmEmailResponse>(
            HttpMethod.Post,
            "api/v1/auth/confirm-email",
            request,
            cancellationToken);

    public Task<ApiResult<ResendConfirmationResponse>> ResendConfirmationAsync(
        ResendConfirmationRequest request,
        CancellationToken cancellationToken = default) =>
        SendAnonymousAsync<ResendConfirmationResponse>(
            HttpMethod.Post,
            "api/v1/auth/resend-confirmation",
            request,
            cancellationToken);

    public async Task<ApiResult<AuthTokenResponse>> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await SendAnonymousAsync<AuthTokenResponse>(
            HttpMethod.Post,
            "api/v1/auth/login",
            request,
            cancellationToken);

        if (result.IsSuccess && result.Value is not null)
        {
            await _session.SetSessionAsync(result.Value, user: null, cancellationToken);
        }

        return result;
    }

    public Task<ApiResult<CurrentUserResponse>> GetMeAsync(CancellationToken cancellationToken = default) =>
        SendAuthenticatedAsync<CurrentUserResponse>(HttpMethod.Get, "api/v1/auth/me", null, cancellationToken);

    public async Task<ApiResult<bool>> LogoutAsync(CancellationToken cancellationToken = default)
    {
        var refresh = _session.Current.RefreshToken;
        if (!string.IsNullOrWhiteSpace(refresh))
        {
            try
            {
                var client = _httpClientFactory.CreateClient(MobileHttpClientNames.Anonymous);
                using var response = await client.PostAsJsonAsync(
                    "api/v1/auth/logout",
                    new LogoutRequest { RefreshToken = refresh },
                    cancellationToken);
                _logger.LogInformation("Logout API completed with status {StatusCode}.", (int)response.StatusCode);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _logger.LogInformation(ex, "Logout API unreachable; clearing local session anyway.");
            }
        }

        await _session.ClearSessionAsync(cancellationToken);
        return ApiResult<bool>.Success(true);
    }

    public Task<ApiResult<PatientProfileResponse>> GetPatientProfileAsync(
        CancellationToken cancellationToken = default) =>
        SendAuthenticatedAsync<PatientProfileResponse>(HttpMethod.Get, "api/v1/patients/me", null, cancellationToken);

    public async Task<ApiResult<PatientProfileResponse>> UpdatePatientProfileAsync(
        UpdatePatientProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(MobileHttpClientNames.Authenticated);
            using var httpRequest = new HttpRequestMessage(HttpMethod.Patch, "api/v1/patients/me")
            {
                Content = JsonContent.Create(request, options: PatchJsonOptions),
            };

            using var response = await client.SendAsync(httpRequest, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var value = await response.Content.ReadFromJsonAsync<PatientProfileResponse>(cancellationToken: cancellationToken);
                if (value is null)
                {
                    return ApiResult<PatientProfileResponse>.Failure(new ApiProblem
                    {
                        Kind = ApiErrorKind.Unknown,
                        Title = "Empty response",
                        StatusCode = (int)response.StatusCode,
                    });
                }

                return ApiResult<PatientProfileResponse>.Success(value);
            }

            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogInformation(
                "Profile update failed. Status={StatusCode} ErrorCodePresent={HasBody}",
                (int)response.StatusCode,
                !string.IsNullOrWhiteSpace(raw));
            return ApiResult<PatientProfileResponse>.Failure(ApiProblemMapper.FromStatusCode(response.StatusCode, raw));
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "Profile update exception.");
            return ApiResult<PatientProfileResponse>.Failure(ApiProblemMapper.FromException(ex));
        }
    }

    private Task<ApiResult<T>> SendAnonymousAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken) =>
        SendAsync<T>(MobileHttpClientNames.Anonymous, method, path, body, cancellationToken);

    private Task<ApiResult<T>> SendAuthenticatedAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken) =>
        SendAsync<T>(MobileHttpClientNames.Authenticated, method, path, body, cancellationToken);

    private async Task<ApiResult<T>> SendAsync<T>(
        string clientName,
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(clientName);
            using var request = new HttpRequestMessage(method, path);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }

            using var response = await client.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                if (typeof(T) == typeof(bool))
                {
                    return ApiResult<T>.Success((T)(object)true);
                }

                // 204 No Content
                if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
                {
                    return ApiResult<T>.Success(default!);
                }

                var value = await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
                if (value is null)
                {
                    return ApiResult<T>.Failure(new ApiProblem
                    {
                        Kind = ApiErrorKind.Unknown,
                        Title = "Empty response",
                        StatusCode = (int)response.StatusCode,
                    });
                }

                return ApiResult<T>.Success(value);
            }

            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogInformation(
                "API call failed. Path={Path} Status={StatusCode} ErrorCodePresent={HasBody}",
                path,
                (int)response.StatusCode,
                !string.IsNullOrWhiteSpace(raw));
            return ApiResult<T>.Failure(ApiProblemMapper.FromStatusCode(response.StatusCode, raw));
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "API call exception. Path={Path}", path);
            return ApiResult<T>.Failure(ApiProblemMapper.FromException(ex));
        }
    }
}
