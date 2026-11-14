using System.ComponentModel.DataAnnotations;

namespace Car_Rental_System_BE.Application.Features.Inspections;

public sealed record Inspection(int Id, int RentalId, string Type, bool? IsOk, decimal? OdometerKm, decimal? FuelBatteryLevel, string? DamageNotes, string? ImageUrls, DateTime? InspectedAt, int? StaffId = null, int? VehicleId = null);
public sealed record InspectionRequest([param:Range(1,int.MaxValue)]int? StaffId,bool IsOk,[param:Range(0,double.MaxValue)]decimal? OdometerKm,[param:Range(0,100)]decimal? FuelBatteryLevel,[param:StringLength(500)]string? DamageNotes,string? ImageUrls);
