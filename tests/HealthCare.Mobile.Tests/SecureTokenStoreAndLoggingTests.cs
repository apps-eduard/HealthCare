using FluentAssertions;
using HealthCare.Contracts.Identity;
using HealthCare.Mobile.Core.Authentication;
using HealthCare.Mobile.Core.Storage;
using HealthCare.Mobile.Tests.Fakes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HealthCare.Mobile.Tests;

public sealed class SecureTokenStoreAndLoggingTests
{
    [Fact]
    public async Task InMemoryStore_ClearSession_Removes_Only_Token_Keys()
    {
        var store = new InMemorySecureTokenStore();
        await store.SetAsync(SecureTokenKeys.AccessToken, "a");
        await store.SetAsync(SecureTokenKeys.RefreshToken, "r");
        await store.SetAsync("unrelated", "x");

        await store.ClearSessionAsync();

        (await store.GetAsync(SecureTokenKeys.AccessToken)).Should().BeNull();
        (await store.GetAsync(SecureTokenKeys.RefreshToken)).Should().BeNull();
        (await store.GetAsync("unrelated")).Should().Be("x");
    }

    [Fact]
    public async Task AuthSessionService_Logs_Do_Not_Contain_Token_Values()
    {
        var store = new InMemorySecureTokenStore();
        var logger = new CapturingLogger<AuthSessionService>();
        var service = new AuthSessionService(store, logger);

        await service.SetSessionAsync(new AuthTokenResponse
        {
            AccessToken = "super-secret-access-token-value",
            RefreshToken = "super-secret-refresh-token-value",
            AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
            RefreshTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1),
        });
        await service.ClearSessionAsync();

        var joined = string.Join('\n', logger.Messages);
        joined.Should().NotContain("super-secret-access-token-value");
        joined.Should().NotContain("super-secret-refresh-token-value");
        joined.Should().Contain("Auth session established");
        joined.Should().Contain("Auth session cleared");
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose()
            {
            }
        }
    }
}
