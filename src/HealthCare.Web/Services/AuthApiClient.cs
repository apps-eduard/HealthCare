using System.Net.Http.Json;
using HealthCare.Contracts.Identity;
using HealthCare.Web.Auth;

namespace HealthCare.Web.Services;

/// <summary>
/// Typed auth client for authenticated profile calls. Login/logout/refresh are BFF-owned and must not return tokens to UI.
/// </summary>
public interface IAuthApiClient
{
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
