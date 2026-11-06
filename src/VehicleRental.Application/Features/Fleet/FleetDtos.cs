using System.ComponentModel.DataAnnotations;
using Car_Rental_System_BE.Application.Validators;

namespace Car_Rental_System_BE.Application.Features.Fleet;

public sealed record VehicleType(int Id, string Name, string? FuelType, string? Type = null);
public sealed record VehicleDetail(
    int? SeatCount = null, string? Transmission = null, decimal? TrunkCapacity = null,
    string? BikeType = null, bool? HasStorageBox = null,
    string? BatteryCapacity = null, string? ChargingType = null, string? ChargingTimeHours = null, decimal? MaxRangeKm = null, string? BatteryType = null,
    IReadOnlyList<string>? GalleryImages = null);
public sealed record Vehicle(int Id, int VehicleTypeId, string VehicleType, int? FacilityId, string? Facility, string Status, string? Brand, string? Model, string? Color, int? YearOfManufacture, decimal? HourlyPrice, decimal? LongtermPrice, string? Description, string? MainImageUrl, string? Location, VehicleDetail? Detail = null, string? LicensePlate = null)
{
    public bool? DriverAvailable { get; init; }
    public bool SupportsSubscription => HourlyPrice.HasValue;
    public IReadOnlyList<string> Features { get; init; } = [];
    public IReadOnlyList<string> AllowedLicenseTypes { get; init; } = [];
    public IReadOnlyList<string> MaintenanceTechnicianEmails { get; init; } = [];
    public int? SeatCount => Detail?.SeatCount;
    public string? Transmission => Detail?.Transmission;
    public string? BatteryCapacity => Detail?.BatteryCapacity;
}
public sealed record VehiclePage(IReadOnlyList<Vehicle> Items, int Page, int PageSize, int TotalCount);
public sealed record VehicleRequest([param:Range(1,int.MaxValue)]int VehicleTypeId,int? FacilityId,[param:Required] string Status,string? Brand,string? Model,string? Color,int? YearOfManufacture,[param:Range(0,double.MaxValue)]decimal? HourlyPrice,[param:Range(0,double.MaxValue)]decimal? LongtermPrice,string? Description,[param:Url]string? MainImageUrl,string? Location,VehicleDetail? Detail=null,[param:StringLength(255)]string? LicensePlate=null, IReadOnlyList<string>? Features=null, [param:LicenseTypes] IReadOnlyList<string>? AllowedLicenseTypes=null){public Vehicle ToModel()=>new(0,VehicleTypeId,"",FacilityId,null,Status.ToLowerInvariant(),Brand,Model,Color,YearOfManufacture,HourlyPrice,LongtermPrice,Description,MainImageUrl,Location,Detail,LicensePlate) { Features = Features ?? [], AllowedLicenseTypes = AllowedLicenseTypes ?? [] };}
public sealed record VehicleTypeRequest([param:Required,StringLength(255)]string Name,[param:Required]string FuelType,[param:Required]string Type);
