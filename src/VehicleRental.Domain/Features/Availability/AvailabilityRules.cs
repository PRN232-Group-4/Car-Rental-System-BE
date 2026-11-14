namespace Car_Rental_System_BE.Domain.Features.Availability;

public enum AvailabilityReason
{
    Booked,
    Maintenance,
    Inactive,
    NotFound
}

public static class BookingStatusRules
{
    /// <summary>Các trạng thái đơn thuê đang giữ xe; phải đồng bộ với <c>fn_rental_is_blocking</c> trong migration 0011.</summary>
    public static bool IsBlocking(string? rentalStatus)
        => rentalStatus is "pending" or "pre_inspection" or "renting" or "post_inspection";

    /// <summary>Các trạng thái xe có thể đưa ra cho khách đặt.</summary>
    public static bool IsBookableVehicleStatus(string? vehicleStatus) => vehicleStatus is "available" or "renting";
}
