using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.DTOs.Operations;

namespace Car_Rental_System_BE.Application.Features.Rentals;

/// <summary>
/// Rental booking and rental reads: FR-042 lead-time rule and payment deadline for a booking, plus the two flat
/// Rental reads. The members sat in <c>OperationsService</c>; the rules below are the same ones, moved unchanged,
/// and the options/clock are resolved exactly as they were there (optional constructor parameters the container
/// fills from <c>Rental:Expiry</c> and the registered <see cref="TimeProvider"/>).
/// </summary>
public sealed class RentalBookingService(IRentalBookingRepository repository, TimeProvider? timeProvider = null, RentalExpiryOptions? expiryOptions = null) : IRentalBookingService
{
    public Task<Rental> CreateRentalAsync(string email, int vehicleId, int? policyId, string rentalType, DateTime start, DateTime end, bool subscription, int? subscriptionId, bool hasDriver, CancellationToken ct, string paymentMethod = "payos")
    {
        if (paymentMethod is not ("payos" or "cash")) throw new ArgumentException("PaymentMethod must be payos or cash.", nameof(paymentMethod));

        // FR-042: giờ nhận xe phải cách thời điểm đặt tối thiểu một khoảng an toàn,
        // và yêu cầu chỉ có một cửa sổ hữu hạn để được duyệt + thanh toán.
        var options = expiryOptions ?? new RentalExpiryOptions();
        var rule = options.ToRule();
        var bookedAt = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        if (!rule.IsStartTimeAllowed(bookedAt, start))
            throw new BusinessRuleException(
                "RENTAL_START_TIME_TOO_SOON",
                $"Giờ nhận xe phải cách thời điểm đặt ít nhất {options.MinimumLeadMinutes} phút. Vui lòng chọn giờ nhận xe muộn hơn.");

        return repository.CreateRentalAsync(email, vehicleId, policyId, rentalType, start, end, subscription, subscriptionId, hasDriver, ct, paymentMethod, rule.ComputeExpireAt(bookedAt));
    }

    public Task<Rental?> GetRentalAsync(int id, CancellationToken ct) =>
        repository.GetRentalAsync(id, ct);

    public Task<Rental?> GetRentalForUserAsync(int id, string email, bool readAll, bool customer, bool driver, CancellationToken ct) =>
        repository.GetRentalForUserAsync(id, email, readAll, customer, driver, ct);
}
