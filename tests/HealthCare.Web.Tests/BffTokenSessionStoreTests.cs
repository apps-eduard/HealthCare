using FluentAssertions;
using HealthCare.Contracts.Identity;
using HealthCare.Web.Auth;
using HealthCare.Web.Configuration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HealthCare.Web.Tests;

public sealed class BffTokenSessionStoreTests
{
    [Fact]
    public async Task Create_Get_Update_And_Remove_Session()
    {
        var sut = CreateStore();
        var userId = Guid.NewGuid();
        var session = await sut.CreateAsync(userId, SampleTokens());

        session.SessionId.Should().NotBeNullOrWhiteSpace();
        session.UserId.Should().Be(userId);

        var loaded = await sut.GetAsync(session.SessionId);
        loaded.Should().NotBeNull();
        loaded!.Tokens.AccessToken.Should().Be("access-1");

        await sut.UpdateTokensAsync(session.SessionId, SampleTokens("access-2", "refresh-2"));
        var updated = await sut.GetAsync(session.SessionId);
        updated!.Tokens.AccessToken.Should().Be("access-2");

        await sut.RemoveAsync(session.SessionId);
        (await sut.GetAsync(session.SessionId)).Should().BeNull();
        (await sut.ExistsAsync(session.SessionId)).Should().BeFalse();
    }

    [Fact]
    public async Task Login_Ticket_Is_Single_Use()
    {
        var sut = CreateStore();
        var session = await sut.CreateAsync(Guid.NewGuid(), SampleTokens());
        var ticket = await sut.CreateLoginTicketAsync(session.SessionId, session.UserId);

        var first = await sut.ConsumeLoginTicketAsync(ticket);
        first.Should().NotBeNull();
        first!.Value.SessionId.Should().Be(session.SessionId);

        var second = await sut.ConsumeLoginTicketAsync(ticket);
        second.Should().BeNull();
    }

    [Fact]
    public async Task Cache_Payload_Is_Not_Plaintext_Tokens()
    {
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var sut = CreateStore(cache);
        var session = await sut.CreateAsync(Guid.NewGuid(), SampleTokens("super-secret-access", "super-secret-refresh"));

        var raw = await cache.GetStringAsync("bff:session:" + session.SessionId);
        raw.Should().NotBeNullOrWhiteSpace();
        raw.Should().NotContain("super-secret-access");
        raw.Should().NotContain("super-secret-refresh");
    }

    [Fact]
    public void Cookie_Principal_Includes_Session_Id_But_Not_Tokens()
    {
        var user = new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "staff@test.local",
            Roles = ["RECEPTIONIST"],
            Permissions = [WebPermissions.AppointmentsRead],
            HasActiveStaffMembership = true,
        };

        var principal = StaffWebAuthCookie.CreatePrincipal(user, "opaque-session-id");
        principal.FindFirst(BffClaimTypes.SessionId)!.Value.Should().Be("opaque-session-id");
        string.Join(',', principal.Claims.Select(c => c.Type + "=" + c.Value))
            .Should().NotContain("eyJ");
        principal.Claims.Select(c => c.Type).Should().NotContain("access_token");
        principal.Claims.Select(c => c.Type).Should().NotContain("refresh_token");
        principal.FindFirst("permission").Should().BeNull();
    }

    [Fact]
    public void ProtectedSession_Token_Store_Type_Is_Removed()
    {
        typeof(IApiTokenStore).Assembly.GetTypes()
            .Select(t => t.Name)
            .Should()
            .NotContain("ProtectedSessionApiTokenStore");
    }

    [Fact]
    public void Browser_Token_Storage_Key_Is_Not_Referenced()
    {
        var webRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "HealthCare.Web"));
        var files = Directory.GetFiles(webRoot, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                        || f.EndsWith(".razor", StringComparison.OrdinalIgnoreCase));

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            text.Should().NotContain(
                "healthcare.staff.auth.tokens",
                because: $"{Path.GetFileName(file)} must not use ProtectedSessionStorage token keys");
            text.Should().NotContain(
                "ProtectedSessionStorage",
                because: $"{Path.GetFileName(file)} must not depend on browser token storage");
        }
    }

    [Fact]
    public void Bff_Options_Defaults_Are_Secure()
    {
        var options = new BffOptions();
        options.SessionIdleMinutes.Should().Be(30);
        options.AbsoluteSessionHours.Should().Be(8);
        options.CookieName.Should().NotBeNullOrWhiteSpace();
        options.RequireHttps.Should().BeTrue();
    }

    [Fact]
    public void Auth_Api_Client_Does_Not_Expose_Login_Returning_Tokens()
    {
        typeof(HealthCare.Web.Services.IAuthApiClient).GetMethods()
            .Select(m => m.Name)
            .Should()
            .NotContain("LoginAsync");
    }

    private static DistributedCacheApiTokenSessionStore CreateStore(IDistributedCache? cache = null)
    {
        cache ??= new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var protection = DataProtectionProvider.Create("HealthCare.Web.Tests");
        return new DistributedCacheApiTokenSessionStore(
            cache,
            protection,
            Options.Create(new BffOptions
            {
                SessionIdleMinutes = 30,
                AbsoluteSessionHours = 8,
                LoginTicketSeconds = 60,
            }));
    }

    private static StoredAuthTokens SampleTokens(string access = "access-1", string refresh = "refresh-1") =>
        new()
        {
            AccessToken = access,
            RefreshToken = refresh,
            AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(15),
            RefreshTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(7),
        };
}
