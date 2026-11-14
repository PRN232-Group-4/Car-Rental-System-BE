using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Features.Availability;
using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Domain.Features.Assignments;
using Car_Rental_System_BE.Domain.Features.Availability;

namespace Car_Rental_System_BE.Application.Features.Assignments;

public sealed class AssignmentService(
    IAssignmentRepository repo,
    IVehicleAvailabilityService availabilityService,
    AssignmentOptions options,
    TimeProvider timeProvider) : IAssignmentService
{
    private readonly AssignmentOptions _options = options;

    public async Task<AssignmentResponse> AssignDriverAsync(DriverAssignmentCommand command, CancellationToken ct)
    {
        var rental = await repo.GetRentalScheduleAsync(command.RentalId, ct)
            ?? throw new NotFoundException("RENTAL_NOT_FOUND", $"Rental {command.RentalId} was not found.");

        if (!RentalAssignmentPolicy.CanAssignOrReplace(rental.Status))
            throw new ConflictException("ILLEGAL_RENTAL_STATE", $"Rental status '{rental.Status}' does not permit driver assignment.");

        var hasExistingAssignment = await repo.HasAnyAssignmentAsync(command.RentalId, ct);
        var reason = string.IsNullOrWhiteSpace(command.Reason)
            ? (hasExistingAssignment ? null : AssignmentReason.Initial)
            : command.Reason;

        if (reason is null || !AssignmentReason.IsValidDriverReason(reason))
            throw new BusinessRuleException("INVALID_ASSIGNMENT_REASON", $"Reason '{command.Reason}' is invalid for driver assignment.", 400);

        if (!hasExistingAssignment && !await repo.HasPassedPreInspectionAsync(command.RentalId, ct))
            throw new ConflictException("PRE_INSPECTION_REQUIRED", "Initial assignment requires a passed pre-rental inspection.");

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var startsAt = rental.StartTime > now ? rental.StartTime : now;
        var endsAt = rental.EndTime;

        if (endsAt <= startsAt)
            throw new BusinessRuleException("RENTAL_ALREADY_ENDED", "Rental period has already elapsed.", 400);

        if (rental.HasDriver)
        {
            if (command.DriverStaffId is null)
                throw new ConflictException("DRIVER_REQUIRED", "This rental requires a driver.");

            var candidates = await repo.ListDriverCandidatesAsync(startsAt, endsAt, _options.DriverBufferMinutes, command.RentalId, ct);
            var driver = candidates.FirstOrDefault(d => d.StaffId == command.DriverStaffId.Value);

            if (driver is null)
                throw new ConflictException("DRIVER_NOT_ELIGIBLE", $"Driver {command.DriverStaffId.Value} does not exist or is not a driver.");

            var (eligible, reasons) = DriverEligibility.Evaluate(driver.IsDriver, driver.IsActive, driver.LicenseExpiry, rental.EndTime);
            var allReasons = new List<string>(reasons);
            if (driver.HasScheduleConflict)
                allReasons.Add(DriverEligibility.Busy);

            if (!eligible || driver.HasScheduleConflict)
                throw new ConflictException("DRIVER_NOT_ELIGIBLE", $"Driver is not eligible: {string.Join(", ", allReasons)}");
        }
        else
        {
            if (command.DriverStaffId is not null)
                throw new ConflictException("DRIVER_NOT_ALLOWED_FOR_SELF_DRIVE", "Self-drive rental cannot have an assigned driver.");

            if (!RentalAssignmentPolicy.IsCustomerLicenseValid(rental.CustomerLicenseStatus, rental.CustomerLicenseExpiry, rental.EndTime))
                throw new ConflictException("CUSTOMER_LICENSE_NOT_VALID", "Customer driver's license is not approved or expires before rental completion.");
        }

        return await repo.AssignDriverAsync(
            rental,
            command.DriverStaffId,
            reason,
            command.Note,
            command.AssignedByEmail,
            startsAt,
            endsAt,
            _options.DriverBufferMinutes,
            ct);
    }

    public async Task<VehicleReplacementResult> ReplaceVehicleAsync(VehicleReplacementCommand command, CancellationToken ct)
    {
        if (!AssignmentReason.IsValidVehicleReason(command.Reason))
            throw new BusinessRuleException("INVALID_ASSIGNMENT_REASON", $"Reason '{command.Reason}' is invalid for vehicle replacement.", 400);

        var rental = await repo.GetRentalScheduleAsync(command.RentalId, ct)
            ?? throw new NotFoundException("RENTAL_NOT_FOUND", $"Rental {command.RentalId} was not found.");

        if (!RentalAssignmentPolicy.CanAssignOrReplace(rental.Status))
            throw new ConflictException("ILLEGAL_RENTAL_STATE", $"Rental status '{rental.Status}' does not permit vehicle replacement.");

        if (command.NewVehicleId == rental.VehicleId)
            throw new BusinessRuleException("SAME_VEHICLE_REPLACEMENT", "Cannot replace a vehicle with the currently assigned vehicle.", 400);

        var isValidType = await repo.VehicleExistsAndActiveAsync(command.NewVehicleId, rental.VehicleTypeId, ct);
        if (!isValidType)
            throw new ConflictException("VEHICLE_TYPE_MISMATCH", "Replacement vehicle must exist, be active/available, and have the exact same vehicle type.");

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var startsAt = rental.StartTime > now ? rental.StartTime : now;
        var endsAt = rental.EndTime;

        if (endsAt <= startsAt)
            throw new BusinessRuleException("RENTAL_ALREADY_ENDED", "Rental period has already elapsed.", 400);

        var availability = await availabilityService.CheckAsync(command.NewVehicleId, new TimeRange(startsAt, endsAt), ct);
        if (!availability.Available)
            throw new ConflictException("VEHICLE_UNAVAILABLE", $"Replacement vehicle {command.NewVehicleId} is unavailable: {availability.Reason}");

        return await repo.ReplaceVehicleAsync(
            rental,
            command.NewVehicleId,
            command.Reason,
            command.Note,
            command.KeepDriver,
            command.AssignedByEmail,
            startsAt,
            endsAt,
            _options.DriverBufferMinutes,
            ct);
    }

    public async Task<IReadOnlyList<AssignmentResponse>> GetAssignmentsAsync(int rentalId, CancellationToken ct)
    {
        var rental = await repo.GetRentalScheduleAsync(rentalId, ct)
            ?? throw new NotFoundException("RENTAL_NOT_FOUND", $"Rental {rentalId} was not found.");

        return await repo.ListAssignmentsAsync(rentalId, ct);
    }

    public async Task<IReadOnlyList<AssignableDriverItem>> ListAssignableDriversAsync(int rentalId, bool eligibleOnly, CancellationToken ct)
    {
        var rental = await repo.GetRentalScheduleAsync(rentalId, ct)
            ?? throw new NotFoundException("RENTAL_NOT_FOUND", $"Rental {rentalId} was not found.");

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var startsAt = rental.StartTime > now ? rental.StartTime : now;
        var endsAt = rental.EndTime;

        if (endsAt <= startsAt)
            throw new BusinessRuleException("RENTAL_ALREADY_ENDED", "Rental period has already elapsed.", 400);

        var candidates = await repo.ListDriverCandidatesAsync(startsAt, endsAt, _options.DriverBufferMinutes, rentalId, ct);
        var result = new List<AssignableDriverItem>();

        foreach (var c in candidates)
        {
            var (eligible, reasons) = DriverEligibility.Evaluate(c.IsDriver, c.IsActive, c.LicenseExpiry, rental.EndTime);
            var allReasons = new List<string>(reasons);
            if (c.HasScheduleConflict)
                allReasons.Add(DriverEligibility.Busy);

            var isFullyEligible = eligible && !c.HasScheduleConflict;

            if (!eligibleOnly || isFullyEligible)
            {
                result.Add(new AssignableDriverItem(
                    c.StaffId,
                    c.FullName,
                    c.AvatarUrl,
                    c.Rating,
                    c.LicenseExpiry,
                    isFullyEligible,
                    allReasons));
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<AssignableVehicleItem>> ListAssignableVehiclesAsync(int rentalId, CancellationToken ct)
    {
        var rental = await repo.GetRentalScheduleAsync(rentalId, ct)
            ?? throw new NotFoundException("RENTAL_NOT_FOUND", $"Rental {rentalId} was not found.");

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var startsAt = rental.StartTime > now ? rental.StartTime : now;
        var endsAt = rental.EndTime;

        if (endsAt <= startsAt)
            throw new BusinessRuleException("RENTAL_ALREADY_ENDED", "Rental period has already elapsed.", 400);

        return await repo.ListAssignableVehiclesAsync(
            rental.VehicleTypeId,
            rental.FacilityId,
            startsAt,
            endsAt,
            _options.DriverBufferMinutes,
            rental.VehicleId,
            ct);
    }

    public Task<DriverShift> SaveDriverShiftAsync(int? id, int driverId, DateTime start, DateTime end, bool cancelled, string actor, CancellationToken ct) =>
        repo.SaveDriverShiftAsync(id, driverId, start, end, cancelled, actor, ct);

    public Task<IReadOnlyList<DriverShift>> ListDriverShiftsAsync(int? driverId, DateTime from, DateTime to, CancellationToken ct) =>
        repo.ListDriverShiftsAsync(driverId, from, to, ct);

    public Task<OperationsPage<DriverScheduleEntry>> DriverScheduleAsync(int? driverId, DateTime from, DateTime to, OperationsPagination pagination, CancellationToken ct)
    {
        pagination.Validate();
        if (to<=from) throw new ArgumentException("End time must be after start time.");
        return repo.DriverScheduleAsync(driverId,Utc(from)!.Value,Utc(to)!.Value,pagination,ct);
    }

    private static DateTime? Utc(DateTime? value) => value is null ? null : DateTime.SpecifyKind(value.Value.Kind == DateTimeKind.Local ? value.Value.ToUniversalTime() : value.Value, DateTimeKind.Unspecified);
}
