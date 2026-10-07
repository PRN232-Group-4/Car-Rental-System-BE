using System.ComponentModel.DataAnnotations;

namespace Car_Rental_System_BE.Application.DTOs.Operations;

public sealed record NameRequest([param:Required,StringLength(255,MinimumLength=2)]string Name,[param:StringLength(255)]string? FuelType=null);
public sealed record FacilityRequest([param:Required,StringLength(255,MinimumLength=2)]string Name,[param:StringLength(255)]string? Address);
public sealed record LicenseRequest([param:Required,StringLength(255)]string Number,[param:Required,StringLength(255)]string Type,DateOnly? IssueDate,DateOnly ExpiryDate,[param:Url] string? ImageUrl);
public sealed record LicenseReviewRequest([param:RegularExpression("^(approved|rejected)$",ErrorMessage="Status must be approved or rejected.")]string Status);
public sealed record VehicleRequest([param:Range(1,int.MaxValue)]int VehicleTypeId,int? FacilityId,[param:Required] string Status,string? Brand,string? Model,string? Color,int? YearOfManufacture,[param:Range(0,double.MaxValue)]decimal? HourlyPrice,[param:Range(0,double.MaxValue)]decimal? LongtermPrice,string? Description,[param:Url]string? MainImageUrl,string? Location,VehicleDetail? Detail=null,[param:StringLength(255)]string? LicensePlate=null){public Vehicle ToModel()=>new(0,VehicleTypeId,"",FacilityId,null,Status.ToLowerInvariant(),Brand,Model,Color,YearOfManufacture,HourlyPrice,LongtermPrice,Description,MainImageUrl,Location,Detail,LicensePlate);}
public sealed record PolicyRequest(int? VehicleTypeId,[param:Required] string Name,[param:Range(0,100)]decimal VatRate,[param:Range(0,100)]decimal OvertimeFeeRate,[param:Range(0,100)]decimal DepositPercentage,bool IsActive=true){public RentalPolicy ToModel()=>new(0,VehicleTypeId,Name,VatRate,OvertimeFeeRate,DepositPercentage,IsActive);}
public sealed record RentalRequest([param:Range(1,int.MaxValue)]int VehicleId,int? PolicyId,DateTime StartTime,DateTime EndTime,int? SubscriptionId,bool HasDriver=false);
public sealed record AssignmentRequest([param:Range(1,int.MaxValue)]int? DriverStaffId);
public sealed record RentalContractRequest([param:Required,Url,StringLength(255)]string ContractLink, DateTime SignedAt);
public sealed record InspectionRequest([param:Range(1,int.MaxValue)]int? StaffId,bool IsOk,[param:Range(0,double.MaxValue)]decimal? OdometerKm,[param:Range(0,100)]decimal? FuelBatteryLevel,[param:StringLength(500)]string? DamageNotes,string? ImageUrls);
public sealed record PlanRequest([param: Required] string Name,string? Description,[param: Range(0.01,double.MaxValue)]decimal Price,[param: Range(1,3650)]int DurationDays,[param: Range(0,double.MaxValue)]decimal? IncludedHours,bool IsActive=true){public SubscriptionPlan ToModel()=>new(0,Name,Description,Price,DurationDays,IncludedHours,IsActive);}
public sealed record MaintenanceRequest([param:Range(1,int.MaxValue)]int VehicleId,int? FacilityId,int? AssignedStaffId,string? Status,string? MaintenanceType,DateOnly? ScheduledDate,bool? IsSafe,string? Description,[param:Range(0,double.MaxValue)]decimal? Cost){public Maintenance ToModel()=>new(0,VehicleId,FacilityId,AssignedStaffId,Status,MaintenanceType,ScheduledDate,IsSafe,Description,Cost);}
public sealed record RateStaffRequest([param:Range(1,int.MaxValue)]int StaffId,[param:Range(1,5)]decimal Rating,[param:StringLength(500)]string? Comment);

public sealed record VehicleTypeRequest([param:Required,StringLength(255)]string Name,[param:Required]string FuelType,[param:Required]string Type);
