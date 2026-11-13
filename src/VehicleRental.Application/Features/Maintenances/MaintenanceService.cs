using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Interfaces.External;

namespace Car_Rental_System_BE.Application.Features.Maintenances;

public sealed class MaintenanceService(IMaintenanceRepository repository, IOperationalNotifications? notifications = null) : IMaintenanceService
{
    public async Task<Maintenance> AssignReportMaintenanceAsync(int reportId, int technicianId, int? facilityId, DateOnly scheduledDate, CancellationToken ct, string? maintenanceType = null, string? description = null, string? actor = null)
    {
        if (reportId < 1 || technicianId < 1 || facilityId is < 1) throw new ArgumentException("Report, technician and facility IDs must be positive.");
        if (maintenanceType is not null && (string.IsNullOrWhiteSpace(maintenanceType) || maintenanceType.Length > 255)) throw new ArgumentException("Maintenance type must contain 1 to 255 characters.");
        if (description is not null && (string.IsNullOrWhiteSpace(description) || description.Length > 4000)) throw new ArgumentException("Description must contain 1 to 4000 characters.");
        var maintenance = await repository.AssignReportMaintenanceAsync(reportId, technicianId, facilityId, scheduledDate, ct, maintenanceType, description, actor);
        if (notifications is not null) await notifications.ReportChangedAsync(reportId, "assigned");
        return maintenance;
    }

    public Task<Maintenance> SaveMaintenanceAsync(int? id, Maintenance maintenance, CancellationToken ct, int? actingTechnicianId = null) =>
        repository.SaveMaintenanceAsync(id, maintenance, ct, actingTechnicianId);

    public Task<IReadOnlyList<Maintenance>> ListMaintenanceAsync(int? vehicleId, string? status, CancellationToken ct) =>
        repository.ListMaintenanceAsync(vehicleId, status, ct);
}
