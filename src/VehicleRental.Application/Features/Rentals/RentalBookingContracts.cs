using Car_Rental_System_BE.Application.DTOs.Operations;

namespace Car_Rental_System_BE.Application.Features.Rentals;

/// <summary>
/// Rental booking and rental reads: creating the request with its price snapshot and payment deadline, the flat
/// rental by id, and the same read narrowed to what the caller is allowed to see. These members used to sit on
/// <c>IOperationsRepository</c>/<c>IOperationsService</c> with the rest of the operations work.
/// </summary>
public interface IRentalBookingRepository
{
    Task<Rental> CreateRentalAsync(string email, int vehicleId, int? policyId, string rentalType, DateTime start, DateTime end, bool subscription, int? subscriptionId, bool hasDriver, CancellationToken ct, string paymentMethod = "payos", DateTime? expireAt = null);
    Task<Rental?> GetRentalAsync(int id, CancellationToken ct);
    Task<Rental?> GetRentalForUserAsync(int id, string email, bool readAll, bool customer, bool driver, CancellationToken ct);
}

public interface IRentalBookingService
{
    Task<Rental> CreateRentalAsync(string email, int vehicleId, int? policyId, string rentalType, DateTime start, DateTime end, bool subscription, int? subscriptionId, bool hasDriver, CancellationToken ct, string paymentMethod = "payos");
    Task<Rental?> GetRentalAsync(int id, CancellationToken ct);
    Task<Rental?> GetRentalForUserAsync(int id, string email, bool readAll, bool customer, bool driver, CancellationToken ct);
}
