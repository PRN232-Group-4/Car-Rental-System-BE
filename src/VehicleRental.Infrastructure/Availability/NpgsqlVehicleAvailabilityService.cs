using Car_Rental_System_BE.Application.Availability;
using Car_Rental_System_BE.Domain.Availability;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Availability;

public sealed class NpgsqlVehicleAvailabilityService(NpgsqlDataSource dataSource, AvailabilityOptions options) : IVehicleAvailabilityService
{
    public async Task<AvailabilityResult> CheckAsync(int vehicleId, TimeRange window, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        return await VehicleAvailabilityChecks.EvaluateAsync(
            connection, null, vehicleId, window, null, options.BufferMinutes, ct);
    }
}
