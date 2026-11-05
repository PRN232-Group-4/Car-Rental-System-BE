using System.Data.Common;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Features.Assignments;

public static class DriverLock
{
    public const int DriverLockCategory = 1002;

    public static async Task AcquireAsync(DbTransaction transaction, int driverStaffId, CancellationToken ct = default)
    {
        if (transaction.Connection is not NpgsqlConnection connection)
            throw new InvalidOperationException("Driver advisory locks require an Npgsql connection.");

        await using var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock($1, $2)", connection, (NpgsqlTransaction)transaction);
        command.Parameters.AddWithValue(DriverLockCategory);
        command.Parameters.AddWithValue(driverStaffId);
        await command.ExecuteNonQueryAsync(ct);
    }
}
