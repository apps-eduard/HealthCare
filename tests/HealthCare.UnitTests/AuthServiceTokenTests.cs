using FluentAssertions;
using HealthCare.Application.Identity;
using HealthCare.Contracts.Identity;
using HealthCare.Domain.Identity;
using HealthCare.Infrastructure.Identity;
using HealthCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HealthCare.UnitTests;

public sealed class AuthServiceTokenTests
{
    private static readonly string SigningKey = "DEV_ONLY_HealthCare_Jwt_Signing_Key_Change_Me_32+";

    [Fact]
    public async Task Login_With_Invalid_Password_Returns_Generic_Invalid_Credentials()
    {
        await using var provider = await BuildProviderAsync();
        var auth = provider.GetRequiredService<IAuthService>();
        var users = provider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "user@example.com",
            UserName = "user@example.com",
            EmailConfirmed = true,
            IsActive = true,
        };
        (await users.CreateAsync(user, "Valid_Pass_1!")).Succeeded.Should().BeTrue();

        var act = () => auth.LoginAsync(
            new LoginRequest { Email = "user@example.com", Password = "Wrong_Pass_1!" },
            new AuthClientContext("127.0.0.1", "test"),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<AuthenticationException>();
        ex.Which.ErrorCode.Should().Be(AuthErrorCodes.InvalidCredentials);
        ex.Which.Title.Should().Be("Invalid email or password.");
    }

    [Fact]
    public async Task Login_With_Unknown_Email_Returns_Same_Generic_Error()
    {
        await using var provider = await BuildProviderAsync();
        var auth = provider.GetRequiredService<IAuthService>();

        var act = () => auth.LoginAsync(
            new LoginRequest { Email = "missing@example.com", Password = "Whatever_1!" },
            new AuthClientContext("127.0.0.1", "test"),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<AuthenticationException>();
        ex.Which.ErrorCode.Should().Be(AuthErrorCodes.InvalidCredentials);
    }

    [Fact]
    public async Task Refresh_Rotates_Token_And_Rejects_Reuse_Of_Old_Token()
    {
        await using var provider = await BuildProviderAsync();
        var auth = provider.GetRequiredService<IAuthService>();
        var users = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = provider.GetRequiredService<HealthCareDbContext>();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "rotator@example.com",
            UserName = "rotator@example.com",
            EmailConfirmed = true,
            IsActive = true,
        };
        (await users.CreateAsync(user, "Valid_Pass_1!")).Succeeded.Should().BeTrue();

        var login = await auth.LoginAsync(
            new LoginRequest { Email = user.Email!, Password = "Valid_Pass_1!" },
            new AuthClientContext("127.0.0.1", "test"));

        var refreshed = await auth.RefreshAsync(
            new RefreshTokenRequest { RefreshToken = login.RefreshToken },
            new AuthClientContext("127.0.0.1", "test"));

        refreshed.AccessToken.Should().NotBeNullOrWhiteSpace();
        refreshed.RefreshToken.Should().NotBe(login.RefreshToken);

        var reuse = () => auth.RefreshAsync(
            new RefreshTokenRequest { RefreshToken = login.RefreshToken },
            new AuthClientContext("127.0.0.1", "test"));

        var ex = await reuse.Should().ThrowAsync<AuthenticationException>();
        ex.Which.ErrorCode.Should().Be(AuthErrorCodes.ReusedRefreshToken);

        var familyId = await db.RefreshTokens
            .Where(t => t.UserId == user.Id)
            .Select(t => t.FamilyId)
            .FirstAsync();

        var activeInFamily = await db.RefreshTokens.CountAsync(t =>
            t.FamilyId == familyId && t.RevokedAtUtc == null);
        activeInFamily.Should().Be(0);
    }

    [Fact]
    public async Task Refresh_Rejects_Expired_Token()
    {
        var time = new MutableTimeProvider(DateTimeOffset.UtcNow);
        await using var provider = await BuildProviderAsync(time);
        var auth = provider.GetRequiredService<IAuthService>();
        var users = provider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "expired@example.com",
            UserName = "expired@example.com",
            EmailConfirmed = true,
            IsActive = true,
        };
        (await users.CreateAsync(user, "Valid_Pass_1!")).Succeeded.Should().BeTrue();

        var login = await auth.LoginAsync(
            new LoginRequest { Email = user.Email!, Password = "Valid_Pass_1!" },
            new AuthClientContext(null, null));

        time.SetUtcNow(time.GetUtcNow().AddDays(30));

        var act = () => auth.RefreshAsync(
            new RefreshTokenRequest { RefreshToken = login.RefreshToken },
            new AuthClientContext(null, null));

        var ex = await act.Should().ThrowAsync<AuthenticationException>();
        ex.Which.ErrorCode.Should().Be(AuthErrorCodes.ExpiredRefreshToken);
    }

    [Fact]
    public async Task Logout_Revokes_Refresh_Token()
    {
        await using var provider = await BuildProviderAsync();
        var auth = provider.GetRequiredService<IAuthService>();
        var users = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = provider.GetRequiredService<HealthCareDbContext>();
        var hasher = provider.GetRequiredService<IRefreshTokenHasher>();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "logout@example.com",
            UserName = "logout@example.com",
            EmailConfirmed = true,
            IsActive = true,
        };
        (await users.CreateAsync(user, "Valid_Pass_1!")).Succeeded.Should().BeTrue();

        var login = await auth.LoginAsync(
            new LoginRequest { Email = user.Email!, Password = "Valid_Pass_1!" },
            new AuthClientContext(null, null));

        await auth.LogoutAsync(new LogoutRequest { RefreshToken = login.RefreshToken });

        var hash = hasher.Hash(login.RefreshToken);
        var stored = await db.RefreshTokens.SingleAsync(t => t.TokenHash == hash);
        stored.IsRevoked.Should().BeTrue();
        stored.RevokedReason.Should().Be("Logout");

        var act = () => auth.RefreshAsync(
            new RefreshTokenRequest { RefreshToken = login.RefreshToken },
            new AuthClientContext(null, null));

        var ex = await act.Should().ThrowAsync<AuthenticationException>();
        ex.Which.ErrorCode.Should().Be(AuthErrorCodes.RevokedRefreshToken);
    }

    private static async Task<ServiceProvider> BuildProviderAsync(TimeProvider? timeProvider = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(timeProvider ?? TimeProvider.System);
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

        services.AddDbContext<HealthCareDbContext>(options =>
        {
            options.UseInMemoryDatabase($"auth-{Guid.NewGuid():N}");
        });

        services
            .AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedEmail = true;
                options.SignIn.RequireConfirmedAccount = true;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
            })
            .AddEntityFrameworkStores<HealthCareDbContext>()
            .AddDefaultTokenProviders();

        services.Configure<JwtOptions>(options =>
        {
            options.Issuer = "HealthCare";
            options.Audience = "HealthCare";
            options.SigningKey = SigningKey;
            options.AccessTokenLifetimeMinutes = 15;
            options.RefreshTokenLifetimeDays = 7;
        });

        services.AddScoped<IAccessTokenService, AccessTokenService>();
        services.AddScoped<IRefreshTokenHasher, RefreshTokenHasher>();
        services.AddScoped<IRefreshTokenGenerator, RefreshTokenGenerator>();
        services.AddScoped<ISecuritySessionInvalidationService, SecuritySessionInvalidationService>();
        services.AddSingleton<IDevelopmentPasswordResetTokenStore, DevelopmentPasswordResetTokenStore>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<AuthService>), NullLogger<AuthService>.Instance);
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<SecuritySessionInvalidationService>), NullLogger<SecuritySessionInvalidationService>.Instance);

        var provider = services.BuildServiceProvider();

        // InMemory + Identity: create schema once and seed roles on the root provider scope.
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
            await db.Database.EnsureCreatedAsync();

            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            foreach (var role in AppRoles.All)
            {
                if (!await roles.RoleExistsAsync(role))
                {
                    var createRole = await roles.CreateAsync(new IdentityRole<Guid>
                    {
                        Id = Guid.NewGuid(),
                        Name = role,
                    });
                    createRole.Succeeded.Should().BeTrue(
                        because: string.Join("; ", createRole.Errors.Select(e => e.Description)));
                }
            }
        }

        return provider;
    }
}
