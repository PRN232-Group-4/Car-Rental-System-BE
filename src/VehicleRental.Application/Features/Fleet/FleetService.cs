using Car_Rental_System_BE.Application.Features.Availability;
using Car_Rental_System_BE.Application.Features.Rentals;
using Car_Rental_System_BE.Domain.Features.Availability;

namespace Car_Rental_System_BE.Application.Features.Fleet;

public sealed class FleetService(IFleetRepository repository, IDriverAvailability? drivers = null, TimeProvider? timeProvider = null) : IFleetService
{
    public Task<IReadOnlyList<VehicleType>> ListVehicleTypesAsync(CancellationToken ct) =>
        repository.ListVehicleTypesAsync(ct);

    public Task<VehicleType> SaveVehicleTypeAsync(int? id, string name, string? fuelType, CancellationToken ct, string? type = null) =>
        repository.SaveVehicleTypeAsync(id, name, fuelType, ct, type);

    public Task DeleteVehicleTypeAsync(int id, CancellationToken ct) =>
        repository.DeleteVehicleTypeAsync(id, ct);

    public async Task<VehiclePage> SearchVehiclesAsync(int? typeId, int? facilityId, string? status, DateTime? from, DateTime? to, int page, int pageSize, CancellationToken ct)
    {
        RentalQueryValidation.Pagination(page, pageSize);
        if (typeId <= 0 || facilityId <= 0) throw new ArgumentException("VehicleTypeId and FacilityId must be positive.");
        if (from.HasValue != to.HasValue || (from.HasValue && to <= from)) throw new ArgumentException("Both from and to are required and to must be after from.");
        var result = await repository.SearchVehiclesAsync(typeId, facilityId, status, from, to, page, pageSize, ct);
        if (from is null || to is null || drivers is null) return result;
        var items = new List<Vehicle>();
        var availableByFacility = new Dictionary<int, bool>();
        foreach (var vehicle in result.Items)
        {
            var key = vehicle.FacilityId ?? 0;
            if (!availableByFacility.TryGetValue(key, out var available))
            {
                available = await drivers.AnyAvailableAsync(new TimeRange(from.Value, to.Value), (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime, ct, vehicle.FacilityId);
                availableByFacility[key] = available;
            }
            items.Add(vehicle with { DriverAvailable = available });
        }
        return result with { Items = items };
    }

    public Task<Vehicle?> GetVehicleAsync(int id, CancellationToken ct) =>
        repository.GetVehicleAsync(id, ct);

    public Task<Vehicle> SaveVehicleAsync(int? id, Vehicle vehicle, CancellationToken ct) =>
        repository.SaveVehicleAsync(id, vehicle with { AllowedLicenseTypes = LicenseTypesAttribute.Normalize(vehicle.AllowedLicenseTypes) }, ct);

    public Task DeleteVehicleAsync(int id, CancellationToken ct) =>
        repository.DeleteVehicleAsync(id, ct);
}
