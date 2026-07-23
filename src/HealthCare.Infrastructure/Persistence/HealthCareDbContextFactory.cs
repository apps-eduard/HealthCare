using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HealthCare.Infrastructure.Persistence;

/// <summary>
/// Design-time factory so EF Core tools do not need to start the full API host.
/// </summary>
public sealed class HealthCareDbContextFactory : IDesignTimeDbContextFactory<HealthCareDbContext>
{
    public HealthCareDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=100.110.26.112;Port=5432;Database=healthcare_db;Username=appuser;Password=123";

        var options = new DbContextOptionsBuilder<HealthCareDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new HealthCareDbContext(options);
    }
}
