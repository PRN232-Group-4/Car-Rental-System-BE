using System.ComponentModel.DataAnnotations;
using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Domain.Features.Availability;

namespace Car_Rental_System_BE.Application.Features.Availability;

public sealed class AvailabilityOptions
{
    public const string SectionName = "Availability";
    /// <summary>Giá trị mặc định của <c>vehicle_bookings.buffer_minutes</c> trong migration 0011; được đối chiếu với database khi khởi động.</summary>
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

/// <summary>Phía đọc của FR-010. Các implementation làm việc với PostgreSQL.</summary>
public interface IVehicleSearchRepository
{
    /// <param name="driverAvailable">Được tính một lần mỗi request bởi <see cref="IDriverAvailability"/>; null khi không được yêu cầu.</param>
    Task<VehicleSearchResult> SearchAsync(VehicleSearchQuery query, int bufferMinutes, bool? driverAvailable, CancellationToken ct);
    Task<ActiveRentalPolicy?> GetActivePolicyAsync(int vehicleTypeId, CancellationToken ct);
    Task<bool> VehicleExistsAsync(int vehicleId, CancellationToken ct);
    Task<IReadOnlyList<BusySlot>> ListBusySlotsAsync(int vehicleId, TimeRange window, CancellationToken ct);
    Task<IReadOnlyList<AffectedRental>> ListAffectedRentalsAsync(int vehicleId, DateTime nowUtc, CancellationToken ct);
}

/// <summary>Nguồn sự thật duy nhất cho câu hỏi "xe này có trống trong khoảng thời gian này không" (tìm kiếm, tạo đơn thuê, đổi xe).</summary>
public interface IVehicleAvailabilityService
{
    Task<AvailabilityResult> CheckAsync(int vehicleId, TimeRange window, CancellationToken ct);

    /// <summary>
    /// Kiểm tra lại trong transaction của bên gọi (sau khi đã khóa xe) và ném
    /// <c>VEHICLE_UNAVAILABLE</c> (409) hoặc <c>VEHICLE_NOT_FOUND</c> (404).
    /// </summary>
}

/// <summary>Cổng kiểm tra tài xế còn trống. FR-014 và BE-33 (lịch nghỉ) dùng chung nguồn này.</summary>
public interface IDriverAvailability
{
    /// <summary>Có ít nhất một tài xế đủ điều kiện còn trống trong <paramref name="window"/> không (hoặc, nếu không có khoảng thời gian, còn hợp lệ tại <paramref name="nowUtc"/>)?</summary>
    Task<bool> AnyAvailableAsync(TimeRange? window, DateTime nowUtc, CancellationToken ct, int? facilityId = null);
}
