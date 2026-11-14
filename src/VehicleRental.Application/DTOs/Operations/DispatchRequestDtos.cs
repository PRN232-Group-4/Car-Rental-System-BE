using System.ComponentModel.DataAnnotations;

namespace Car_Rental_System_BE.Application.DTOs.Operations;

public sealed record IncidentReportRequest([param:Required,RegularExpression("^(damage|breakdown)$")] string Type,
    [param:Required,StringLength(4000,MinimumLength=1)] string Description, [param:StringLength(8000)] string? ImageUrls=null);
public sealed record ReportMaintenanceRequest([param:Range(1,int.MaxValue)] int AssignedStaffId,
    [param:Range(1,int.MaxValue)] int? FacilityId, DateOnly ScheduledDate,
    [param:StringLength(255,MinimumLength=1)] string? MaintenanceType = null,
    [param:StringLength(4000,MinimumLength=1)] string? Description = null);
public sealed record VehicleReplacementRequest([param:Range(1,int.MaxValue)] int VehicleId,
    [param:Range(1,int.MaxValue)] int? DriverStaffId, [param:Range(1,int.MaxValue)] int? ReportId);
