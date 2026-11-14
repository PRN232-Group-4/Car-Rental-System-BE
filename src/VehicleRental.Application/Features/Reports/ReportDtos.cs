using System.ComponentModel.DataAnnotations;

namespace Car_Rental_System_BE.Application.Features.Reports;

public sealed record IncidentReport(int Id, int RentalId, int VehicleId, int ReporterStaffId, string Type,
    string Description, string Status, int? MaintenanceId, string? ImageUrls, DateTime? CreatedAt = null, DateTime? ResolvedAt = null, ReportCustomer? Customer = null, ReportRental? Rental = null, ReportMaintenance? Maintenance = null, IReadOnlyList<ReportHistory>? History = null, IReadOnlyList<ReportNote>? Notes = null);
public sealed record IncidentReportRequest([param:Required,RegularExpression("^(damage|breakdown)$")] string Type,
    [param:Required,StringLength(4000,MinimumLength=1)] string Description, [param:StringLength(8000)] string? ImageUrls=null);
public sealed record ReportMaintenanceRequest([param:Range(1,int.MaxValue)] int AssignedStaffId,
    [param:Range(1,int.MaxValue)] int? FacilityId, DateOnly ScheduledDate,
    [param:StringLength(255,MinimumLength=1)] string? MaintenanceType = null,
    [param:StringLength(4000,MinimumLength=1)] string? Description = null);
public sealed record ReportStatusRequest([param: Required, RegularExpression("^(in_progress|rejected)$")] string Status,
    [param: Required, StringLength(2000, MinimumLength=1)] string Reason);
public sealed record ReportCustomer(int Id, string? FullName, string? Phone, string Email);
public sealed record ReportRental(int Id, string? VehicleName, string? LicensePlate, DateTime? Start, DateTime? End, string Status, string? DriverName);
public sealed record ReportMaintenance(string? Type, string? Status, decimal? Cost, DateOnly? ScheduledDate);
public sealed record ReportHistory(long Id, string? FromStatus, string? Status, string? Reason, string? By, DateTime At);
public sealed record ReportNote(long Id, string Text, string? By, DateTime At);
