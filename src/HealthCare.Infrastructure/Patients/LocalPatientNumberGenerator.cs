using HealthCare.Application.Patients;
using HealthCare.Domain.Patients;
using HealthCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace HealthCare.Infrastructure.Patients;

public sealed class LocalPatientNumberGenerator : ILocalPatientNumberGenerator
{
    private readonly HealthCareDbContext _dbContext;
    private readonly ILogger<LocalPatientNumberGenerator> _logger;

    public LocalPatientNumberGenerator(
        HealthCareDbContext dbContext,
        ILogger<LocalPatientNumberGenerator> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<string> AllocateNextAsync(Guid clinicId, CancellationToken cancellationToken = default)
    {
        // Prefer atomic upsert on PostgreSQL; fall back for in-memory unit tests.
        if (_dbContext.Database.IsNpgsql())
        {
            var connection = _dbContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await _dbContext.Database.OpenConnectionAsync(cancellationToken);
            }

            await using var command = connection.CreateCommand();
            command.Transaction = _dbContext.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText =
                """
                INSERT INTO "ClinicPatientNumberSequences" ("ClinicId", "LastValue")
                VALUES (@clinicId, 1)
                ON CONFLICT ("ClinicId") DO UPDATE
                SET "LastValue" = "ClinicPatientNumberSequences"."LastValue" + 1
                RETURNING "LastValue";
                """;

            var parameter = command.CreateParameter();
            parameter.ParameterName = "clinicId";
            parameter.Value = clinicId;
            command.Parameters.Add(parameter);

            var result = await command.ExecuteScalarAsync(cancellationToken);
            var value = Convert.ToInt64(result);
            return Format(value);
        }

        var sequence = await _dbContext.ClinicPatientNumberSequences
            .SingleOrDefaultAsync(s => s.ClinicId == clinicId, cancellationToken);

        if (sequence is null)
        {
            sequence = new ClinicPatientNumberSequence
            {
                ClinicId = clinicId,
                LastValue = 1,
            };
            _dbContext.ClinicPatientNumberSequences.Add(sequence);
        }
        else
        {
            sequence.LastValue++;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogDebug("Allocated local patient number for clinic {ClinicId}", clinicId);
        return Format(sequence.LastValue);
    }

    internal static string Format(long value) => $"P-{value:D6}";
}
