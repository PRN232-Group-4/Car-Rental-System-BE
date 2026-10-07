namespace Car_Rental_System_BE.Application.DTOs.Operations;

public sealed record DriverShift(int Id, int DriverStaffId, DateTime StartTime, DateTime EndTime, string Status);
public sealed record IncidentReport(int Id, int RentalId, int VehicleId, int ReporterStaffId, string Type,
    string Description, string Status, int? MaintenanceId, string? ImageUrls);
