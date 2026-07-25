using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Text;
using HealthCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Testcontainers.PostgreSql;

namespace HealthCare.EndToEndTests;

/// <summary>
/// Shared E2E host: temporary PostgreSQL + separate API/Web processes on ephemeral ports.
/// Separate processes are used so Blazor Server SignalR and BFF cookies behave like production.
/// </summary>
public sealed class E2eHostFixture : IAsyncLifetime
{
    public const string EndToEndEnvVar = "HEALTHCARE_END_TO_END_TEST_HOST";
    public const string SkipStaticLoggerEnvVar = "HEALTHCARE_SKIP_STATIC_LOGGER_FLUSH";
    public const string HeadedEnvVar = "HEALTHCARE_E2E_HEADED";

    private PostgreSqlContainer? _postgres;
    private DotNetAppProcess? _api;
    private DotNetAppProcess? _web;

    public string WebBaseUrl { get; private set; } = string.Empty;

    public string ApiBaseUrl { get; private set; } = string.Empty;

    public string ConnectionString { get; private set; } = string.Empty;

    public string RepoRoot { get; } = ResolveRepoRoot();

    public string ArtifactsDirectory { get; private set; } = string.Empty;

    public E2eSeedUsers Users { get; } = E2eSeedUsers.DevelopmentDefaults;

    public async Task InitializeAsync()
    {
        Environment.SetEnvironmentVariable(EndToEndEnvVar, "true");
        Environment.SetEnvironmentVariable(SkipStaticLoggerEnvVar, "true");

        ArtifactsDirectory = Path.Combine(RepoRoot, "tests", "HealthCare.EndToEndTests", "artifacts");
        Directory.CreateDirectory(ArtifactsDirectory);

        if (string.Equals(Environment.GetEnvironmentVariable(HeadedEnvVar), "true", StringComparison.OrdinalIgnoreCase))
        {
            Environment.SetEnvironmentVariable("HEADED", "1");
        }

        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("healthcare_e2e")
            .WithUsername("healthcare")
            .WithPassword("healthcare_e2e")
            .Build();

        await _postgres.StartAsync();
        ConnectionString = _postgres.GetConnectionString();

        await using (var migrateDb = new HealthCareDbContext(
                         new DbContextOptionsBuilder<HealthCareDbContext>()
                             .UseNpgsql(ConnectionString)
                             .Options))
        {
            await migrateDb.Database.MigrateAsync();
        }

        var apiPort = FreeTcpPort();
        var webPort = FreeTcpPort();
        ApiBaseUrl = $"http://127.0.0.1:{apiPort}";
        WebBaseUrl = $"http://127.0.0.1:{webPort}";

        var apiProject = Path.Combine(RepoRoot, "src", "HealthCare.Api", "HealthCare.Api.csproj");
        var webProject = Path.Combine(RepoRoot, "src", "HealthCare.Web", "HealthCare.Web.csproj");

        _api = await DotNetAppProcess.StartAsync(
            apiProject,
            ApiBaseUrl,
            new Dictionary<string, string?>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Development",
                ["ASPNETCORE_URLS"] = ApiBaseUrl,
                ["ConnectionStrings__DefaultConnection"] = ConnectionString,
                [EndToEndEnvVar] = "true",
                [SkipStaticLoggerEnvVar] = "true",
                ["HealthCare__EndToEndTestHost"] = "true",
                ["Hangfire__Enabled"] = "false",
                ["Hangfire__ScheduleRecurringJobs"] = "false",
                ["Hangfire__Dashboard__Enabled"] = "false",
                ["Hangfire__ServerName"] = "healthcare-e2e",
                ["Jwt__Issuer"] = "HealthCare",
                ["Jwt__Audience"] = "HealthCare",
                ["Jwt__SigningKey"] = "DEV_ONLY_HealthCare_Jwt_Signing_Key_Change_Me_32+",
            },
            readyPath: "/health",
            readyTimeout: TimeSpan.FromMinutes(2));

        _web = await DotNetAppProcess.StartAsync(
            webProject,
            WebBaseUrl,
            new Dictionary<string, string?>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Development",
                ["ASPNETCORE_URLS"] = WebBaseUrl,
                [EndToEndEnvVar] = "true",
                [SkipStaticLoggerEnvVar] = "true",
                ["Api__BaseUrl"] = ApiBaseUrl + "/",
                ["Bff__RequireHttps"] = "false",
                ["Bff__CookieName"] = "HealthCare.Staff.Auth",
            },
            readyPath: "/login",
            readyTimeout: TimeSpan.FromMinutes(2));
    }

    public async Task DisposeAsync()
    {
        if (_web is not null)
        {
            await _web.DisposeAsync();
        }

        if (_api is not null)
        {
            await _api.DisposeAsync();
        }

        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }
    }

    public async Task CaptureFailureArtifactsAsync(IPage page, string testName)
    {
        try
        {
            var safe = string.Join("_", testName.Split(Path.GetInvalidFileNameChars()));
            var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
            var screenshot = Path.Combine(ArtifactsDirectory, $"{safe}_{stamp}.png");
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshot, FullPage = true });
        }
        catch
        {
            // Best-effort diagnostics only.
        }
    }

    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HealthCare.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate HealthCare.sln from the E2E test assembly.");
    }

    private static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

public sealed record E2eSeedUsers(
    string OrganizationAdminEmail,
    string OrganizationAdminPassword,
    string ClinicAdminEmail,
    string ClinicAdminPassword,
    string DoctorEmail,
    string DoctorPassword,
    string PatientEmail,
    string PatientPassword,
    string PlatformAdminEmail,
    string PlatformAdminPassword)
{
    public static E2eSeedUsers DevelopmentDefaults { get; } = new(
        OrganizationAdminEmail: "orgadmin@healthcare.local",
        OrganizationAdminPassword: "ChangeMe_OrgAdmin_1!",
        ClinicAdminEmail: "clinicadmin@healthcare.local",
        ClinicAdminPassword: "ChangeMe_ClinicAdmin_1!",
        DoctorEmail: "doctor.a@healthcare.local",
        DoctorPassword: "ChangeMe_DoctorA_1!",
        PatientEmail: "patient@healthcare.local",
        PatientPassword: "ChangeMe_Patient_1!",
        PlatformAdminEmail: "admin@healthcare.local",
        PlatformAdminPassword: "ChangeMe_Admin_1!");
}

public sealed class DotNetAppProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly StringBuilder _stdout = new();
    private readonly StringBuilder _stderr = new();

    private DotNetAppProcess(Process process)
    {
        _process = process;
    }

    public static async Task<DotNetAppProcess> StartAsync(
        string projectPath,
        string baseUrl,
        IReadOnlyDictionary<string, string?> environment,
        string readyPath,
        TimeSpan readyTimeout)
    {
        var start = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{projectPath}\" --no-launch-profile",
            WorkingDirectory = Path.GetDirectoryName(projectPath)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var (key, value) in environment)
        {
            start.Environment[key] = value;
        }

        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        var host = new DotNetAppProcess(process);
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (host._stdout)
                {
                    host._stdout.AppendLine(e.Data);
                }
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (host._stderr)
                {
                    host._stderr.AppendLine(e.Data);
                }
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start process for {projectPath}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTimeOffset.UtcNow + readyTimeout;
        Exception? last = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException(
                    $"Process for {projectPath} exited early ({process.ExitCode}). Stderr:{Environment.NewLine}{host.GetStderr()}");
            }

            try
            {
                using var response = await client.GetAsync(baseUrl.TrimEnd('/') + readyPath);
                if ((int)response.StatusCode is >= 200 and < 500)
                {
                    return host;
                }
            }
            catch (Exception ex)
            {
                last = ex;
            }

            await Task.Delay(500);
        }

        await host.DisposeAsync();
        throw new TimeoutException(
            $"Timed out waiting for {baseUrl}{readyPath}. Last error: {last}. Stderr:{Environment.NewLine}{host.GetStderr()}");
    }

    public string GetStderr()
    {
        lock (_stderr)
        {
            return _stderr.ToString();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }
        }
        catch
        {
            // ignore shutdown races
        }
        finally
        {
            _process.Dispose();
        }
    }
}

[CollectionDefinition(E2eCollection.Name, DisableParallelization = true)]
public sealed class E2eCollection : ICollectionFixture<E2eHostFixture>
{
    public const string Name = "HealthcareE2E";
}
