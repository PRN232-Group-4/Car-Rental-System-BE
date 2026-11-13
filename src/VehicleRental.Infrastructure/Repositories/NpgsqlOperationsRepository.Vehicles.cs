using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Car_Rental_System_BE.Infrastructure.Repositories;

public sealed partial class NpgsqlOperationsRepository
{
    private static async Task<(string Type, string Fuel)> GetVehicleCategoryAsync(VehicleRentalDbContext db, int typeId, CancellationToken ct)
    {
        var row = await db.LockByKeyAsync<VehicleTypesRow>(typeId, ct, RowLock.Share)
            ?? throw new ArgumentException("VehicleTypeId was not found.");
        var type = row.Type?.Trim().ToLowerInvariant();
        var fuel = row.FuelType?.Trim().ToLowerInvariant();
        if (type is not ("car" or "motorbike") || fuel is not ("gasoline" or "electric"))
            throw new ArgumentException("Vehicle type must have type car/motorbike and fuel_type gasoline/electric.");
        return (type, fuel);
    }

    private static void ValidateVehicleDetail(string type, string fuel, VehicleDetail? detail)
    {
        if (detail is null) return;
        if (type == "car" && (detail.BikeType is not null || detail.HasStorageBox.HasValue))
            throw new ArgumentException("Car detail cannot contain motorbike fields.");
        if (type == "motorbike" && (detail.SeatCount.HasValue || detail.Transmission is not null || detail.TrunkCapacity.HasValue))
            throw new ArgumentException("Motorbike detail cannot contain car fields.");
        if (fuel == "gasoline" && (detail.BatteryCapacity is not null || detail.ChargingType is not null || detail.ChargingTimeHours is not null || detail.MaxRangeKm.HasValue || detail.BatteryType is not null))
            throw new ArgumentException("Gasoline vehicles cannot contain battery or charging fields.");
        if (detail.SeatCount <= 0 || detail.TrunkCapacity < 0 || detail.MaxRangeKm < 0)
            throw new ArgumentException("SeatCount must be positive; capacities and range cannot be negative.");
    }

    /// <summary>Removes detail rows that do not belong to the vehicle's (new) category; null type removes all.</summary>
    private static async Task CleanVehicleDetailsAsync(VehicleRentalDbContext db, int id, string? type, string? fuel, CancellationToken ct)
    {
        if (type != "car") await db.Set<CarDetailRow>().Where(d => d.VehicleId == id).ExecuteDeleteAsync(ct);
        if (type != "motorbike") await db.Set<MotorbikeDetailRow>().Where(d => d.VehicleId == id).ExecuteDeleteAsync(ct);
        if (!(type == "car" && fuel == "electric")) await db.Set<ElectricCarDetailRow>().Where(d => d.VehicleId == id).ExecuteDeleteAsync(ct);
        if (!(type == "motorbike" && fuel == "electric")) await db.Set<ElectricMotorbikeDetailRow>().Where(d => d.VehicleId == id).ExecuteDeleteAsync(ct);
    }

    public async Task DeleteVehicleAsync(int id, CancellationToken ct)
    {
        await using var cn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        await using var db = VehicleRentalDbContext.Create(cn, tx);
        await LockOperationsAsync(db, ct);
        if (await db.LockByKeyAsync<VehiclesRow>(id, ct) is null)
            throw new KeyNotFoundException("Vehicle was not found.");
        var used = await db.Set<RentalsRow>().AnyAsync(x => x.VehicleId == id, ct)
            || await db.Set<VehicleAssignmentsRow>().AnyAsync(x => x.VehicleId == id, ct)
            || await db.Set<VehicleMaintenanceRow>().AnyAsync(x => x.VehicleId == id, ct)
            || await db.Set<InspectionsRow>().AnyAsync(x => x.VehicleId == id, ct)
            || await db.Set<ReportsRow>().AnyAsync(x => x.VehicleId == id, ct)
            || await db.Set<VehicleReplacementsRow>().AnyAsync(x => x.OldVehicleId == id || x.NewVehicleId == id, ct);
        if (used) throw new InvalidOperationException("Cannot delete a vehicle with rental or operational history.");
        await CleanVehicleDetailsAsync(db, id, null, null, ct);
        await db.Set<VehicleImgsRow>().Where(x => x.VehicleId == id).ExecuteDeleteAsync(ct);
        await db.Set<VehiclesRow>().Where(x => x.Id == id).ExecuteDeleteAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<VehicleType> SaveVehicleTypeAsync(int? id, string name, string? fuel, CancellationToken ct, string? type = null)
    {
        type = type?.Trim().ToLowerInvariant();
        fuel = fuel?.Trim().ToLowerInvariant();
        if (type is not ("car" or "motorbike") || fuel is not ("gasoline" or "electric"))
            throw new ArgumentException("Type must be car/motorbike and FuelType must be gasoline/electric.");
        await using var cn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        await using var db = VehicleRentalDbContext.Create(cn, tx);
        await LockOperationsAsync(db, ct);
        VehicleType saved;
        if (id is not null)
        {
            var current = await db.LockByKeyAsync<VehicleTypesRow>(id.Value, ct) ?? throw new KeyNotFoundException("Vehicle type was not found.");
            var changed = current.Type?.Trim().ToLowerInvariant() != type || current.FuelType?.Trim().ToLowerInvariant() != fuel;
            if (changed && await db.Set<VehiclesRow>().AnyAsync(v => v.VehicleTypeId == id, ct))
                throw new InvalidOperationException("Cannot change the category of a vehicle type in use. Create a new type and update each vehicle instead.");
            await db.Set<VehicleTypesRow>().Where(t => t.Id == id).ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Name, name)
                .SetProperty(t => t.FuelType, fuel)
                .SetProperty(t => t.Type, type), ct);
            saved = new VehicleType(id.Value, name, fuel, type);
        }
        else
        {
            var row = new VehicleTypesRow { Name = name, FuelType = fuel, Type = type };
            await db.InsertAsync(row, ct);
            saved = new VehicleType(row.Id, name, fuel, type);
        }
        await tx.CommitAsync(ct);
        return saved;
    }
}
