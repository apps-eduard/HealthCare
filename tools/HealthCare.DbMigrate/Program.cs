using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using HealthCare.Infrastructure.DependencyInjection;
using HealthCare.Infrastructure.Identity;
using HealthCare.Infrastructure.Patients;
using HealthCare.Infrastructure.Persistence;

namespace HealthCare.DbMigrate;

/// <summary>
/// One-shot migrate (+ optional E2E/Development seed) for k3s Jobs.
/// Refuses connection strings whose Database name is not exactly health_care_e2e
/// unless HEALTHCARE_DBMIGRATE_ALLOW_ANY_DATABASE=true (local tooling only).
/// </summary>
public static class Program
{
    private const string AllowedDatabase = "health_care_e2e";

    public static async Task<int> Main(string[] args)
    {
        var seed = args.Any(a => string.Equals(a, "--seed", StringComparison.OrdinalIgnoreCase));
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine("ConnectionStrings__DefaultConnection is required.");
            return 2;
        }

        if (!TryGetDatabaseName(connectionString, out var databaseName))
        {
            Console.Error.WriteLine("Connection string must include Database=…");
            return 3;
        }

        var allowAny = string.Equals(
            Environment.GetEnvironmentVariable("HEALTHCARE_DBMIGRATE_ALLOW_ANY_DATABASE"),
            "true",
            StringComparison.OrdinalIgnoreCase);

        if (!allowAny && !string.Equals(databaseName, AllowedDatabase, StringComparison.Ordinal))
        {
            Console.Error.WriteLine(
                $"Refusing to run: Database must be exactly '{AllowedDatabase}' (got '{databaseName}').");
            return 4;
        }

        if (!allowAny && IsForbiddenDatabaseName(databaseName))
        {
            Console.Error.WriteLine($"Refusing to run: database name '{databaseName}' is forbidden.");
            return 5;
        }

        var builder = Host.CreateApplicationBuilder(args);
        builder.Configuration["ConnectionStrings:DefaultConnection"] = connectionString;
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"))
            && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")))
        {
            builder.Environment.EnvironmentName = "E2E";
        }

        builder.Services.AddInfrastructure(builder.Configuration);
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(o =>
        {
            o.SingleLine = true;
            o.TimestampFormat = "HH:mm:ss ";
        });

        using var host = builder.Build();
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbMigrate");

        logger.LogInformation("Applying EF Core migrations…");
        await db.Database.MigrateAsync();
        logger.LogInformation("Migrations applied successfully.");

        if (seed)
        {
            logger.LogInformation("Seeding identity roles and E2E users…");
            await host.Services.SeedIdentityRolesAsync();
            await host.Services.SeedDevelopmentAdminAsync();
            await host.Services.SeedDevelopmentPatientAsync();
            logger.LogInformation("Seed completed (idempotent).");
        }

        return 0;
    }

    private static bool TryGetDatabaseName(string connectionString, out string databaseName)
    {
        databaseName = string.Empty;
        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var idx = part.IndexOf('=');
            if (idx <= 0)
            {
                continue;
            }

            var key = part[..idx].Trim();
            var value = part[(idx + 1)..].Trim();
            if (key.Equals("Database", StringComparison.OrdinalIgnoreCase)
                || key.Equals("Initial Catalog", StringComparison.OrdinalIgnoreCase))
            {
                databaseName = value;
                return !string.IsNullOrWhiteSpace(databaseName);
            }
        }

        return false;
    }

    private static bool IsForbiddenDatabaseName(string databaseName)
    {
        var lower = databaseName.ToLowerInvariant();
        return lower is "health_care_dev" or "health_care_staging"
               || lower.Contains("production", StringComparison.Ordinal)
               || lower is "prod" or "production"
               || (lower.Contains("staging", StringComparison.Ordinal) && lower != AllowedDatabase)
               || (lower.Contains("dev", StringComparison.Ordinal) && lower != AllowedDatabase);
    }
}
