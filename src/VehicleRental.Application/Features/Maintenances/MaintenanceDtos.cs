using System.ComponentModel.DataAnnotations;

namespace Car_Rental_System_BE.Application.Features.Maintenances;

public sealed record Maintenance(int Id, int VehicleId, int? FacilityId, int? AssignedStaffId, string? Status, string? MaintenanceType, DateOnly? ScheduledDate, bool? IsSafe, string? Description, decimal? Cost, DateTime? CreatedAt = null, int? RentalId = null, string? AssignedStaffEmail = null, string? FacilityName = null);
public sealed record MaintenanceRequest([param:Range(1,int.MaxValue)]int VehicleId,int? FacilityId,int? AssignedStaffId,string? Status,string? MaintenanceType,DateOnly? ScheduledDate,bool? IsSafe,string? Description,[param:Range(0,double.MaxValue)]decimal? Cost){public Maintenance ToModel()=>new(0,VehicleId,FacilityId,AssignedStaffId,Status,MaintenanceType,ScheduledDate,IsSafe,Description,Cost);}
