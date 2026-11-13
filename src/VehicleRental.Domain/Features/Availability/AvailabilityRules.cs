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
    /// <summary>Rental statuses that hold the vehicle; must stay in sync with <c>fn_rental_is_blocking</c> in migration 0011.</summary>
    public static bool IsBlocking(string? rentalStatus)
        => rentalStatus is "pending" or "pre_inspection" or "renting" or "post_inspection";

    /// <summary>Vehicle statuses that can be offered to customers.</summary>
    public static bool IsBookableVehicleStatus(string? vehicleStatus) => vehicleStatus is "available" or "renting";
}
