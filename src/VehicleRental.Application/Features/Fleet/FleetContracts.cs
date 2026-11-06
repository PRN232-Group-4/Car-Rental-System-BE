namespace Car_Rental_System_BE.Application.Features.Fleet;

public interface IFleetRepository
{
    Task<IReadOnlyList<VehicleType>> ListVehicleTypesAsync(CancellationToken ct);
    Task<VehicleType> SaveVehicleTypeAsync(int? id, string name, string? fuelType, CancellationToken ct, string? type = null);
    Task DeleteVehicleTypeAsync(int id, CancellationToken ct);
    Task<VehiclePage> SearchVehiclesAsync(int? typeId, int? facilityId, string? status, DateTime? from, DateTime? to, int page, int pageSize, CancellationToken ct);
    Task<Vehicle?> GetVehicleAsync(int id, CancellationToken ct);
    Task<Vehicle> SaveVehicleAsync(int? id, Vehicle vehicle, CancellationToken ct);
    Task DeleteVehicleAsync(int id, CancellationToken ct);
}

public interface IFleetService
{
    Task<IReadOnlyList<VehicleType>> ListVehicleTypesAsync(CancellationToken ct);
    Task<VehicleType> SaveVehicleTypeAsync(int? id, string name, string? fuelType, CancellationToken ct, string? type = null);
    Task DeleteVehicleTypeAsync(int id, CancellationToken ct);
    Task<VehiclePage> SearchVehiclesAsync(int? typeId, int? facilityId, string? status, DateTime? from, DateTime? to, int page, int pageSize, CancellationToken ct);
    Task<Vehicle?> GetVehicleAsync(int id, CancellationToken ct);
    Task<Vehicle> SaveVehicleAsync(int? id, Vehicle vehicle, CancellationToken ct);
    Task DeleteVehicleAsync(int id, CancellationToken ct);
}
