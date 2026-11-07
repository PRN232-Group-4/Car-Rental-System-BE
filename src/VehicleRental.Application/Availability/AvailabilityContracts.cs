using System.ComponentModel.DataAnnotations;
using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Domain.Availability;

namespace Car_Rental_System_BE.Application.Availability;

public sealed class AvailabilityOptions
{
    public const string SectionName = "Availability";
    /// <summary>Default of <c>vehicle_bookings.buffer_minutes</c> in migration 0011; checked against the database at startup.</summary>
    public const int DefaultBufferMinutes = 60;

    [Range(0, 1440)] public int BufferMinutes { get; init; } = DefaultBufferMinutes;
    [Range(1, 1440)] public int MinHourlyMinutes { get; init; } = 60;
    [Range(1, 3650)] public int MaxWindowDays { get; init; } = 366;
    [Range(1, 3650)] public int MaxBusySlotDays { get; init; } = 92;
    [Range(1, 3650)] public int DefaultBusySlotDays { get; init; } = 31;
    [Range(0, 60)] public int PastToleranceMinutes { get; init; } = 5;
}

public sealed record VehicleSearchQuery(
    DateTime? From, DateTime? To, string RentalType = "hourly", int? VehicleTypeId = null, int? FacilityId = null,
    string? Q = null, string? Brand = null, decimal? MinPrice = null, decimal? MaxPrice = null, int? SeatCount = null,
    string? Transmission = null, string? BikeType = null, decimal? MinRangeKm = null, int? YearFrom = null,
    string? FuelType = null, bool? HasDriver = null, string Sort = "recommended", int Page = 1, int PageSize = 20,
    string? Category = null)
{
    public static readonly IReadOnlySet<string> SortValues = new HashSet<string> { "recommended", "price_asc", "price_desc", "newest", "name_asc" };
    public static readonly IReadOnlySet<string> CategoryValues = new HashSet<string> { "car", "electric_car", "motorbike", "electric_motorbike" };
    public TimeRange? Window => From is { } f && To is { } t ? new TimeRange(f, t) : null;
}

public sealed record VehicleTypeRef(int Id, string Name, string? FuelType, string? Type = null);
public sealed record FacilityRef(int Id, string Name);
public sealed record VehicleSummary(int? SeatCount, string? Transmission, string? BikeType, bool? HasStorageBox, decimal? MaxRangeKm, string? BatteryCapacity);
public sealed record VehicleBadges(bool Electric, bool? DriverAvailable, bool SubscriptionEligible);
public sealed record VehicleSearchItem(
    int Id, VehicleTypeRef VehicleType, string? Brand, string? Model, int? YearOfManufacture, string? Color,
    string? MainImageUrl, string? Location, FacilityRef? Facility, decimal? PriceHourly, decimal? PriceLongterm,
    VehicleSummary Summary, VehicleBadges Badges, double? DurationHours)
{
    public IReadOnlyList<string> AllowedLicenseTypes { get; init; } = [];
}
public sealed record AppliedWindow(DateTime From, DateTime To, int BufferMinutes);
public sealed record VehicleSearchResult(IReadOnlyList<VehicleSearchItem> Items, int Page, int PageSize, int TotalCount, AppliedWindow? AppliedWindow);

public sealed record ConflictWindow(DateTime? From, DateTime? To);
public sealed record AvailabilityResult(bool Available, string? Reason, ConflictWindow? ConflictsWith)
{
    public static AvailabilityResult Free { get; } = new(true, null, null);
    public static string ReasonCode(AvailabilityReason reason) => reason switch
    {
        AvailabilityReason.Booked => "BOOKED",
        AvailabilityReason.Maintenance => "MAINTENANCE",
        AvailabilityReason.Inactive => "INACTIVE",
        _ => "NOT_FOUND"
    };
    public static AvailabilityResult Blocked(AvailabilityReason reason, ConflictWindow? conflict = null) => new(false, ReasonCode(reason), conflict);
}

public sealed record BusySlot(DateTime From, DateTime To, string Kind);
public sealed record ActiveRentalPolicy(int Id, int? VehicleTypeId, string Name, decimal VatRate, decimal DepositPercentage, decimal OvertimeFeeRate);
public sealed record AffectedRental(int RentalId, string? CustomerName, DateTime Start, DateTime End, string Status);

/// <summary>Read side of FR-010. Implementations talk to PostgreSQL.</summary>
public interface IVehicleSearchRepository
{
    /// <param name="driverAvailable">Evaluated once per request by <see cref="IDriverAvailability"/>; null when not requested.</param>
    Task<VehicleSearchResult> SearchAsync(VehicleSearchQuery query, int bufferMinutes, bool? driverAvailable, CancellationToken ct);
    Task<ActiveRentalPolicy?> GetActivePolicyAsync(int vehicleTypeId, CancellationToken ct);
    Task<bool> VehicleExistsAsync(int vehicleId, CancellationToken ct);
    Task<IReadOnlyList<BusySlot>> ListBusySlotsAsync(int vehicleId, TimeRange window, CancellationToken ct);
    Task<IReadOnlyList<AffectedRental>> ListAffectedRentalsAsync(int vehicleId, DateTime nowUtc, CancellationToken ct);
}

/// <summary>Single source of truth for "is this vehicle free in this window" (search, rental creation, vehicle swap).</summary>
public interface IVehicleAvailabilityService
{
    Task<AvailabilityResult> CheckAsync(int vehicleId, TimeRange window, CancellationToken ct);

    /// <summary>
    /// Re-checks inside the caller's transaction (after taking the vehicle lock) and throws
    /// <c>VEHICLE_UNAVAILABLE</c> (409) or <c>VEHICLE_NOT_FOUND</c> (404).
    /// </summary>
}

/// <summary>Port for driver availability. FR-014 and BE-33 (shifts, days off) plug into the same source.</summary>
public interface IDriverAvailability
{
    /// <summary>Is at least one qualified driver free during <paramref name="window"/> (or, with no window, valid as of <paramref name="nowUtc"/>)?</summary>
    Task<bool> AnyAvailableAsync(TimeRange? window, DateTime nowUtc, CancellationToken ct, int? facilityId = null);
}
