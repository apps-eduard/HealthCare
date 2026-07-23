using System.Net.Http.Json;
using HealthCare.Contracts.Identity;
using HealthCare.Web.Auth;

namespace HealthCare.Web.Services;

public interface IAuthApiClient
{
    Task<AuthTokenResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    Task<CurrentUserResponse> GetMeAsync(CancellationToken cancellationToken = default);

    Task<CurrentUserPermissionsResponse> GetPermissionsAsync(CancellationToken cancellationToken = default);
}

public sealed class AuthApiClient : IAuthApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;

    public AuthApiClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<AuthTokenResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi.Anonymous");
        using var response = await client.PostAsJsonAsync("api/v1/auth/login", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ApiProblemException.FromResponseAsync(response, cancellationToken);
        }

        var tokens = await response.Content.ReadFromJsonAsync<AuthTokenResponse>(cancellationToken);
        return tokens ?? throw new ApiProblemException(500, "Invalid login response", null, null);
    }

    public async Task<CurrentUserResponse> GetMeAsync(CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        using var response = await client.GetAsync("api/v1/auth/me", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ApiProblemException.FromResponseAsync(response, cancellationToken);
        }

        var me = await response.Content.ReadFromJsonAsync<CurrentUserResponse>(cancellationToken);
        return me ?? throw new ApiProblemException(500, "Invalid profile response", null, null);
    }

    public async Task<CurrentUserPermissionsResponse> GetPermissionsAsync(CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        using var response = await client.GetAsync("api/v1/auth/me/permissions", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ApiProblemException.FromResponseAsync(response, cancellationToken);
        }

        var perms = await response.Content.ReadFromJsonAsync<CurrentUserPermissionsResponse>(cancellationToken);
        return perms ?? throw new ApiProblemException(500, "Invalid permissions response", null, null);
    }
}
