using System.ComponentModel.DataAnnotations;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Domain.Features.Availability;

namespace Car_Rental_System_BE.Application.Features.Assignments;

public sealed class AssignmentOptions
{
    public const string SectionName = "Assignment";

    [Range(0, 1440)]
    public int DriverBufferMinutes { get; init; } = 60;

    public bool LicenseClassRulesEnabled { get; init; } = false;
}

public sealed record DriverAssignmentCommand(
    int RentalId,
    int? DriverStaffId,
    string Reason,
    string? Note,
    string AssignedByEmail);

public sealed record VehicleReplacementCommand(
    int RentalId,
    int NewVehicleId,
    string Reason,
    string? Note,
    bool KeepDriver,
    string AssignedByEmail);

public sealed record AssignmentResponse(
    int Id,
    int RentalId,
    int VehicleId,
    int? DriverStaffId,
    string? DriverName,
    string Status,
    string? Reason,
    string? Note,
    int? SupersedesId,
    DateTime StartsAt,
    DateTime EndsAt,
    DateTime? EndedAt,
    DateTime AssignedAt,
    string? AssignedBy,
    string? VehicleName = null,
    string? LicensePlate = null,
    int BufferMinutes = 0);

public sealed record VehicleReplacementResult(
    AssignmentResponse Assignment,
    bool RequiresInspection,
    int? IncidentMaintenanceId);

public sealed record AssignableDriverItem(
    int StaffId,
    string FullName,
    string? AvatarUrl,
    decimal? Rating,
    DateOnly? LicenseExpiry,
    bool Eligible,
    IReadOnlyList<string> Reasons);

public sealed record AssignableVehicleItem(
    int VehicleId,
    int VehicleTypeId,
    string VehicleTypeName,
    string? Brand,
    string? Model,
    int? Year,
    string? Color,
    int? FacilityId,
    string? FacilityName,
    decimal? PriceHourly,
    decimal? PriceLongterm,
    bool IsSameFacility);

public sealed record RentalScheduleInfo(
    int RentalId,
    int VehicleId,
    int VehicleTypeId,
    int? FacilityId,
    string Status,
    bool HasDriver,
    int CustomerId,
    DateTime StartTime,
    DateTime EndTime,
    string? CustomerLicenseStatus,
    DateOnly? CustomerLicenseExpiry);

public sealed record DriverCandidateRaw(
    int StaffId,
    string FullName,
    string? AvatarUrl,
    decimal? Rating,
    bool IsDriver,
    bool IsActive,
    DateOnly? LicenseExpiry,
    bool HasScheduleConflict);

public interface IAssignmentRepository
{
    Task<RentalScheduleInfo?> GetRentalScheduleAsync(int rentalId, CancellationToken ct);
    Task<bool> HasPassedPreInspectionAsync(int rentalId, CancellationToken ct);
    Task<bool> HasAnyAssignmentAsync(int rentalId, CancellationToken ct);
    Task<IReadOnlyList<AssignmentResponse>> ListAssignmentsAsync(int rentalId, CancellationToken ct);

    Task<AssignmentResponse> AssignDriverAsync(
        RentalScheduleInfo rental,
        int? driverStaffId,
        string reason,
        string? note,
        string assignedByEmail,
        DateTime startsAt,
        DateTime endsAt,
        int bufferMinutes,
        CancellationToken ct);

    Task<VehicleReplacementResult> ReplaceVehicleAsync(
        RentalScheduleInfo rental,
        int newVehicleId,
        string reason,
        string? note,
        bool keepDriver,
        string assignedByEmail,
        DateTime startsAt,
        DateTime endsAt,
        int bufferMinutes,
        CancellationToken ct);

    Task<IReadOnlyList<DriverCandidateRaw>> ListDriverCandidatesAsync(
        DateTime windowStart,
        DateTime windowEnd,
        int bufferMinutes,
        int? excludeRentalId,
        CancellationToken ct);

    Task<IReadOnlyList<AssignableVehicleItem>> ListAssignableVehiclesAsync(
        int vehicleTypeId,
        int? currentFacilityId,
        DateTime windowStart,
        DateTime windowEnd,
        int bufferMinutes,
        int excludeVehicleId,
        CancellationToken ct);

    Task<bool> VehicleExistsAndActiveAsync(int vehicleId, int requiredTypeId, CancellationToken ct);

    /// <summary>The paged duty queue: one row per active assignment overlapping [from, to).</summary>
    Task<OperationsPage<DriverScheduleEntry>> DriverScheduleAsync(int? driverId, DateTime from, DateTime to, OperationsPagination pagination, CancellationToken ct);
}

public interface IAssignmentService
{
    Task<AssignmentResponse> AssignDriverAsync(DriverAssignmentCommand command, CancellationToken ct);
    Task<VehicleReplacementResult> ReplaceVehicleAsync(VehicleReplacementCommand command, CancellationToken ct);
    Task<IReadOnlyList<AssignmentResponse>> GetAssignmentsAsync(int rentalId, CancellationToken ct);
    Task<IReadOnlyList<AssignableDriverItem>> ListAssignableDriversAsync(int rentalId, bool eligibleOnly, CancellationToken ct);
    Task<IReadOnlyList<AssignableVehicleItem>> ListAssignableVehiclesAsync(int rentalId, CancellationToken ct);
    Task<OperationsPage<DriverScheduleEntry>> DriverScheduleAsync(int? driverId, DateTime from, DateTime to, OperationsPagination pagination, CancellationToken ct);
}
