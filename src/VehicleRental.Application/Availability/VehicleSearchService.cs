using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Domain.Availability;

namespace Car_Rental_System_BE.Application.Availability;

/// <summary>Use cases of FR-010: public search, availability, busy slots, active policy, affected rentals.</summary>
public sealed class VehicleSearchService(
    IVehicleSearchRepository repository,
    IVehicleAvailabilityService availability,
    IDriverAvailability drivers,
    AvailabilityOptions options,
    TimeProvider time)
{
    public const string InvalidTimeRange = "INVALID_TIME_RANGE";

    public async Task<VehicleSearchResult> SearchAsync(VehicleSearchQuery query, CancellationToken ct)
    {
        var now = Now();
        if (query.Sort == "nearest")
            throw new BusinessRuleException("SORT_NOT_SUPPORTED", "Sorting by distance is not supported yet.");
        if (!VehicleSearchQuery.SortValues.Contains(query.Sort))
            throw new BusinessRuleException("INVALID_SORT", "Sort must be one of recommended, price_asc, price_desc, newest.");
        if (query.RentalType is not ("hourly" or "longterm"))
            throw new BusinessRuleException("INVALID_RENTAL_TYPE", "rentalType must be 'hourly' or 'longterm'.");

        var window = ValidateWindow(query.From, query.To, query.RentalType, now);
        bool? driverAvailable = null;
        if (query.HasDriver == true)
        {
            driverAvailable = await drivers.AnyAvailableAsync(window, now, ct);
            if (driverAvailable == false)
                return new VehicleSearchResult([], query.Page, query.PageSize, 0, Applied(window));
        }

        return await repository.SearchAsync(query, options.BufferMinutes, driverAvailable, ct);
    }

    public async Task<AvailabilityResult> CheckAvailabilityAsync(int vehicleId, DateTime? from, DateTime? to, CancellationToken ct)
    {
        if (from is null || to is null) throw new BusinessRuleException(InvalidTimeRange, "Both 'from' and 'to' are required.");
        var window = ValidateWindow(from, to, "hourly", Now())!.Value;
        return await availability.CheckAsync(vehicleId, window, ct);
    }

    public async Task<IReadOnlyList<BusySlot>> BusySlotsAsync(int vehicleId, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var start = from ?? Now();
        var end = to ?? start.AddDays(options.DefaultBusySlotDays);
        if (end <= start) throw new BusinessRuleException(InvalidTimeRange, "'from' must be earlier than 'to'.");
        if (end - start > TimeSpan.FromDays(options.MaxBusySlotDays))
            throw new BusinessRuleException(InvalidTimeRange, $"The window must not exceed {options.MaxBusySlotDays} days.");
        if (!await repository.VehicleExistsAsync(vehicleId, ct)) throw VehicleNotFound();
        return await repository.ListBusySlotsAsync(vehicleId, new TimeRange(start, end), ct);
    }

    public async Task<ActiveRentalPolicy> ActivePolicyAsync(int vehicleTypeId, CancellationToken ct)
        => await repository.GetActivePolicyAsync(vehicleTypeId, ct)
           ?? throw new NotFoundException("POLICY_NOT_FOUND", "No active rental policy applies to this vehicle type.");

    public async Task<IReadOnlyList<AffectedRental>> AffectedRentalsAsync(int vehicleId, CancellationToken ct)
    {
        if (!await repository.VehicleExistsAsync(vehicleId, ct)) throw VehicleNotFound();
        return await repository.ListAffectedRentalsAsync(vehicleId, Now(), ct);
    }

    internal static NotFoundException VehicleNotFound() => new("VEHICLE_NOT_FOUND", "Vehicle was not found.");

    private DateTime Now() => time.GetUtcNow().UtcDateTime;

    private AppliedWindow? Applied(TimeRange? w) => w is { } r ? new(r.From, r.To, options.BufferMinutes) : null;

    private TimeRange? ValidateWindow(DateTime? from, DateTime? to, string rentalType, DateTime now)
    {
        if (from is null && to is null) return null;
        if (from is null || to is null) throw new BusinessRuleException(InvalidTimeRange, "'from' and 'to' must be provided together.");
        if (from >= to) throw new BusinessRuleException(InvalidTimeRange, "'from' must be earlier than 'to'.");
        if (to - from > TimeSpan.FromDays(options.MaxWindowDays))
            throw new BusinessRuleException(InvalidTimeRange, $"The window must not exceed {options.MaxWindowDays} days.");
        if (from < now.AddMinutes(-options.PastToleranceMinutes))
            throw new BusinessRuleException(InvalidTimeRange, "'from' must not be in the past.");
        if (rentalType == "hourly" && to - from < TimeSpan.FromMinutes(options.MinHourlyMinutes))
            throw new BusinessRuleException(InvalidTimeRange, $"Hourly rentals must last at least {options.MinHourlyMinutes} minutes.");
        return new TimeRange(from.Value, to.Value);
    }
}
