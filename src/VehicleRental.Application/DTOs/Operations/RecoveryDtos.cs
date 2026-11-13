namespace Car_Rental_System_BE.Application.DTOs.Operations;

public sealed record IncidentReport(int Id, int RentalId, int VehicleId, int ReporterStaffId, string Type,
    string Description, string Status, int? MaintenanceId, string? ImageUrls, DateTime? CreatedAt = null, DateTime? ResolvedAt = null, ReportCustomer? Customer = null, ReportRental? Rental = null, ReportMaintenance? Maintenance = null, IReadOnlyList<ReportHistory>? History = null, IReadOnlyList<ReportNote>? Notes = null);
