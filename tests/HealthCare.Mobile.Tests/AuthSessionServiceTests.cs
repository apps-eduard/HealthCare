using FluentAssertions;
using HealthCare.Contracts.Identity;
using HealthCare.Mobile.Core.Authentication;
using HealthCare.Mobile.Core.Storage;
using HealthCare.Mobile.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace HealthCare.Mobile.Tests;

public sealed class AuthSessionServiceTests
{
    [Fact]
    public async Task InitializeAsync_Restores_Session_From_Secure_Store()
    {
        var store = new InMemorySecureTokenStore();
        var tokens = CreateTokens("access-1", "refresh-1");
        var service = new AuthSessionService(store, NullLogger<AuthSessionService>.Instance);
        await service.SetSessionAsync(tokens);

        var restored = new AuthSessionService(store, NullLogger<AuthSessionService>.Instance);
        await restored.InitializeAsync();

        restored.IsAuthenticated.Should().BeTrue();
        restored.Current.AccessToken.Should().Be("access-1");
        restored.Current.RefreshToken.Should().Be("refresh-1");
        store.Snapshot.Should().ContainKey(SecureTokenKeys.AccessToken);
    }

    [Fact]
    public async Task InitializeAsync_Clears_Corrupted_Partial_Session()
    {
        var store = new InMemorySecureTokenStore();
        await store.SetAsync(SecureTokenKeys.AccessToken, "orphan-access");
        var service = new AuthSessionService(store, NullLogger<AuthSessionService>.Instance);

        await service.InitializeAsync();

        service.IsAuthenticated.Should().BeFalse();
        store.Snapshot.Should().NotContainKey(SecureTokenKeys.AccessToken);
    }

    [Fact]
    public async Task ClearSessionAsync_Removes_Tokens_From_Store()
    {
        var store = new InMemorySecureTokenStore();
        var service = new AuthSessionService(store, NullLogger<AuthSessionService>.Instance);
        await service.SetSessionAsync(CreateTokens("a", "r"));

        await service.ClearSessionAsync();

        service.IsAuthenticated.Should().BeFalse();
        store.Snapshot.Should().BeEmpty();
    }

    [Fact]
    public async Task SetSessionAsync_Does_Not_Require_Fresh_Access_Expiry_For_Authenticated_Flag()
    {
        var store = new InMemorySecureTokenStore();
        var service = new AuthSessionService(store, NullLogger<AuthSessionService>.Instance);
        var tokens = new AuthTokenResponse
        {
            AccessToken = "access-expired",
            RefreshToken = "refresh-ok",
            AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
            RefreshTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(7),
        };

        await service.SetSessionAsync(tokens);

        service.IsAuthenticated.Should().BeTrue();
        service.Current.IsAccessTokenExpiredOrExpiring.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateCurrentUserAsync_Sets_Patient_Linkage_Hook()
    {
        var store = new InMemorySecureTokenStore();
        var service = new AuthSessionService(store, NullLogger<AuthSessionService>.Instance);
        await service.SetSessionAsync(CreateTokens("a", "r"));

        await service.UpdateCurrentUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "patient@example.com",
            Roles = ["PATIENT"],
            PatientId = Guid.NewGuid(),
            HasLinkedPatient = true,
            Permissions = [],
        });

        service.Current.HasLinkedPatient.Should().BeTrue();
        service.Current.CurrentUser!.Email.Should().Be("patient@example.com");
    }

    private static AuthTokenResponse CreateTokens(string access, string refresh) =>
        new()
        {
            AccessToken = access,
            RefreshToken = refresh,
            AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
            RefreshTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(7),
        };
}
