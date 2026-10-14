using Car_Rental_System_BE.Application.DTOs.Operations;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Repositories;

public sealed partial class NpgsqlOperationsRepository
{
    private static string ElectricDetailTable(string type) => type == "car" ? "electric_car_detail" : "electric_motorbike_detail";

    private static async Task<(string Type, string Fuel)> GetVehicleCategoryAsync(NpgsqlConnection cn, NpgsqlTransaction tx, int typeId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("SELECT lower(trim(type)),lower(trim(fuel_type)) FROM vehicle_types WHERE id=$1 FOR SHARE", cn, tx);
        Add(cmd, typeId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new ArgumentException("VehicleTypeId was not found.");
        var type = S(reader, 0);
        var fuel = S(reader, 1);
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

    private static async Task CleanVehicleDetailsAsync(NpgsqlConnection cn, NpgsqlTransaction tx, int id, string? type, string? fuel, CancellationToken ct)
    {
        foreach (var table in new[] { "car_detail", "motorbike_detail", "electric_car_detail", "electric_motorbike_detail" })
        {
            if ((type == "car" && table == "car_detail") || (type == "motorbike" && table == "motorbike_detail") ||
                (type is not null && fuel == "electric" && table == ElectricDetailTable(type))) continue;
            await using var cmd = new NpgsqlCommand($"DELETE FROM {table} WHERE vehicle_id=$1", cn, tx);
            Add(cmd, id);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task DeleteVehicleAsync(int id, CancellationToken ct)
    {
        await using var cn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        await LockOperationsAsync(cn, tx, ct);
        if (await ScalarAsync(cn, tx, "SELECT id FROM vehicles WHERE id=$1 FOR UPDATE", id, ct) is not int)
            throw new KeyNotFoundException("Vehicle was not found.");
        var used = await ScalarAsync(cn, tx, """
            SELECT EXISTS(SELECT 1 FROM rentals WHERE vehicle_id=$1)
                OR EXISTS(SELECT 1 FROM vehicle_assignments WHERE vehicle_id=$1)
                OR EXISTS(SELECT 1 FROM vehicle_maintenance WHERE vehicle_id=$1)
                OR EXISTS(SELECT 1 FROM inspections WHERE vehicle_id=$1)
                OR EXISTS(SELECT 1 FROM reports WHERE vehicle_id=$1)
                OR EXISTS(SELECT 1 FROM vehicle_replacements WHERE old_vehicle_id=$1 OR new_vehicle_id=$1)
            """, id, ct);
        if (used is true) throw new InvalidOperationException("Cannot delete a vehicle with rental or operational history.");
        await CleanVehicleDetailsAsync(cn, tx, id, null, null, ct);
        await using var images = new NpgsqlCommand("DELETE FROM vehicle_imgs WHERE vehicle_id=$1", cn, tx);
        Add(images, id);
        await images.ExecuteNonQueryAsync(ct);
        await using var vehicle = new NpgsqlCommand("DELETE FROM vehicles WHERE id=$1", cn, tx);
        Add(vehicle, id);
        await vehicle.ExecuteNonQueryAsync(ct);
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
        await LockOperationsAsync(cn, tx, ct);
        if (id is not null)
        {
            await using var current = new NpgsqlCommand("SELECT lower(trim(type)),lower(trim(fuel_type)) FROM vehicle_types WHERE id=$1 FOR UPDATE", cn, tx);
            Add(current, id.Value);
            await using var reader = await current.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) throw new KeyNotFoundException("Vehicle type was not found.");
            var changed = S(reader, 0) != type || S(reader, 1) != fuel;
            await reader.CloseAsync();
            if (changed && await ScalarAsync(cn, tx, "SELECT EXISTS(SELECT 1 FROM vehicles WHERE vehicle_type_id=$1)", id.Value, ct) is true)
                throw new InvalidOperationException("Cannot change the category of a vehicle type in use. Create a new type and update each vehicle instead.");
        }
        var sql = id is null
            ? "INSERT INTO vehicle_types(name,fuel_type,type) VALUES($1,$2,$3) RETURNING id,name,fuel_type,type"
            : "UPDATE vehicle_types SET name=$1,fuel_type=$2,type=$3 WHERE id=$4 RETURNING id,name,fuel_type,type";
        await using var cmd = new NpgsqlCommand(sql, cn, tx);
        Add(cmd, name, fuel, type);
        if (id is not null) Add(cmd, id.Value);
        await using var result = await cmd.ExecuteReaderAsync(ct);
        await result.ReadAsync(ct);
        var saved = new VehicleType(result.GetInt32(0), result.GetString(1), S(result, 2), S(result, 3));
        await result.CloseAsync();
        await tx.CommitAsync(ct);
        return saved;
    }}
