using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.Features.Fleet;
using Car_Rental_System_BE.Infrastructure.Features.Availability;
using Car_Rental_System_BE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Features.Fleet;

public sealed class NpgsqlFleetRepository(NpgsqlDataSource dataSource) : IFleetRepository
{
    public async Task<IReadOnlyList<VehicleType>> ListVehicleTypesAsync(CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await db.Set<VehicleTypesRow>().AsNoTracking().OrderBy(x => x.Name)
            .Select(x => new VehicleType(x.Id, x.Name, x.FuelType, x.Type)).ToListAsync(ct);
    }
    public async Task DeleteVehicleTypeAsync(int id,CancellationToken ct)
    {
        await using var cn=await dataSource.OpenConnectionAsync(ct);
        await using var tx=await cn.BeginTransactionAsync(ct);
        await using var db = VehicleRentalDbContext.Create(cn, tx);
        // Schema has no FKs: refuse instead of orphaning vehicles that still reference this type.
        if(await db.Set<VehiclesRow>().AnyAsync(v => v.VehicleTypeId == id, ct))
            throw new ConflictException("VEHICLE_TYPE_IN_USE", "Loại phương tiện đang được dùng cho xe trong đội. Hãy chuyển các xe sang loại khác trước khi xóa.");
        await using var c=new NpgsqlCommand("DELETE FROM vehicle_types WHERE id=$1",cn,tx);
        c.Parameters.AddWithValue(id);
        if(await c.ExecuteNonQueryAsync(ct)!=1)throw new KeyNotFoundException();
        await tx.CommitAsync(ct);
    }
    public async Task<VehiclePage> SearchVehiclesAsync(int? type, int? facility, string? status, DateTime? from, DateTime? to, int page, int size, CancellationToken ct)
    {
        from = from is { } fromValue ? NpgsqlVehicleSearchRepository.Timestamp(fromValue) : null;
        to = to is { } toValue ? NpgsqlVehicleSearchRepository.Timestamp(toValue) : null;
        await using var db = VehicleRentalDbContext.Create(dataSource);
        var query = db.Set<VehiclesRow>().AsNoTracking().Where(v =>
            (type == null || v.VehicleTypeId == type) && (facility == null || v.FacilityId == facility) &&
            (status == null || v.Status == status));
        if (from != null)
            query = query.Where(v => v.Status == "available" &&
                !db.Set<VehicleMaintenanceRow>().Any(m => m.VehicleId == v.Id &&
                    ((m.Status ?? "open") != "completed" && (m.Status ?? "open") != "cancelled" || m.IsSafe == false)) &&
                !(from r in db.Set<RentalsRow>()
                    join h in db.Set<HourRentalDetailRow>() on r.Id equals h.RentalId into hourly
                    from h in hourly.DefaultIfEmpty()
                    join l in db.Set<LongtermRentalContractRow>() on r.Id equals l.RentalId into longterm
                    from l in longterm.DefaultIfEmpty()
                    where r.VehicleId == v.Id && r.Status != "cancelled" && r.Status != "completed" && r.Status != "expired" &&
                        ((DateTime?)h.StartTime ?? l.StartTime) < to && ((DateTime?)h.EndTime ?? l.EndTime) > @from
                    select r.Id).Any());
        var total = await query.CountAsync(ct);
        var offset = ((long)page - 1) * size;
        var items = offset > int.MaxValue ? new List<Vehicle>() : await LoadVehiclesAsync(db,
            query.OrderByDescending(v => v.CreatedAt).ThenByDescending(v => v.Id).Skip((int)offset).Take(size), ct);
        return new(items, page, size, total);
    }

    public async Task<Vehicle?> GetVehicleAsync(int id, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        var vehicle = (await LoadVehiclesAsync(db, db.Set<VehiclesRow>().Where(v => v.Id == id), ct)).FirstOrDefault();
        return vehicle is null ? null : vehicle with { Detail = await LoadVehicleDetailAsync(id, ct) };
    }

    private static async Task<List<Vehicle>> LoadVehiclesAsync(VehicleRentalDbContext db, IQueryable<VehiclesRow> vehicles, CancellationToken ct)
    {
        var rows = await (from v in vehicles.AsNoTracking()
            join t in db.Set<VehicleTypesRow>() on v.VehicleTypeId equals t.Id
            join f in db.Set<FacilitiesRow>() on v.FacilityId equals f.Id into facilities
            from f in facilities.DefaultIfEmpty()
            select new
            {
                v.Id, v.VehicleTypeId, VehicleType = t.Name, v.FacilityId, Facility = f.Name,
                Status = v.Status ?? "available", v.Brand, v.Model, v.Color, v.YearOfManufacture,
                v.RentalPriceHourly, v.RentalPriceLongterm, v.Description, v.MainImgUrl, v.CurrentLocation,
                v.LicensePlate, v.Features, v.AllowedLicenseTypes,
                SeatCount = t.Type!.Trim().ToLower() == "car" ? db.Set<CarDetailRow>().Where(d => d.VehicleId == v.Id).Select(d => d.SeatCount).FirstOrDefault() : null,
                Transmission = t.Type!.Trim().ToLower() == "car" ? db.Set<CarDetailRow>().Where(d => d.VehicleId == v.Id).Select(d => d.Transmission).FirstOrDefault() : null,
                BatteryCapacity = t.FuelType!.Trim().ToLower() == "electric" ?
                    (t.Type!.Trim().ToLower() == "car" ? db.Set<ElectricCarDetailRow>().Where(d => d.VehicleId == v.Id).Select(d => d.BatteryCapacity).FirstOrDefault() :
                        db.Set<ElectricMotorbikeDetailRow>().Where(d => d.VehicleId == v.Id).Select(d => d.BatteryCapacity).FirstOrDefault()) : null
            }).ToListAsync(ct);
        var ids = rows.Select(v => v.Id).ToArray();
        var technicians = await (from m in db.Set<VehicleMaintenanceRow>().AsNoTracking()
            join st in db.Set<StaffsRow>() on m.AssignedStaffId equals st.Id
            join u in db.Set<UsersRow>() on st.UserId equals u.Id
            where ids.Contains(m.VehicleId) &&
                ((m.Status ?? "open") == "open" || m.Status == "scheduled" || m.Status == "in_progress" || m.Status == "completed" && m.IsSafe == false) &&
                u.IsActive && (from ur in db.Set<UserRolesRow>() join role in db.Set<RolesRow>() on ur.RoleId equals role.Id
                    where ur.UserId == u.Id && role.Name == "Maintenance" select ur.UserId).Any()
            select new { m.VehicleId, u.Email }).Distinct().OrderBy(x => x.Email).ToListAsync(ct);
        var emails = technicians.ToLookup(x => x.VehicleId, x => x.Email);
        return rows.Select(v => new Vehicle(v.Id, v.VehicleTypeId, v.VehicleType, v.FacilityId, v.Facility,
            v.Status, v.Brand, v.Model, v.Color, v.YearOfManufacture, v.RentalPriceHourly, v.RentalPriceLongterm,
            v.Description, v.MainImgUrl, v.CurrentLocation,
            new VehicleDetail(SeatCount: v.SeatCount, Transmission: v.Transmission, BatteryCapacity: v.BatteryCapacity), v.LicensePlate)
            { Features = v.Features, AllowedLicenseTypes = v.AllowedLicenseTypes, MaintenanceTechnicianEmails = emails[v.Id].ToArray() }).ToList();
    }

    public async Task<Vehicle> SaveVehicleAsync(int? id,Vehicle v,CancellationToken ct){
        await using var cn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        await LockOperationsAsync(cn, tx, ct);
        if (id is not null && await ScalarAsync(cn, tx, "SELECT id FROM vehicles WHERE id=$1 FOR UPDATE", id.Value, ct) is not int)
            throw new KeyNotFoundException("Vehicle was not found.");
        await using var db = VehicleRentalDbContext.Create(cn, tx);
        var category = await GetVehicleCategoryAsync(cn, tx, v.VehicleTypeId, ct);
        ValidateVehicleDetail(category.Type, category.Fuel, v.Detail);
        if(v.Features is null || v.Features.Count>100 || v.Features.Any(x=>string.IsNullOrWhiteSpace(x) || x.Length>100))
            throw new ArgumentException("Features must have at most 100 non-empty values of up to 100 characters.");
        if (v.FacilityId is not null && !await db.Set<FacilitiesRow>().AnyAsync(f => f.Id == v.FacilityId, ct))
            throw new ArgumentException("Facility was not found.");
        var sql=id is null?"INSERT INTO vehicles(vehicle_type_id,facility_id,status,brand,model,color,year_of_manufacture,rental_price_hourly,rental_price_longterm,description,main_img_url,current_location,license_plate,features,allowed_license_types) VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15) RETURNING id":"UPDATE vehicles SET vehicle_type_id=$1,facility_id=$2,status=$3,brand=$4,model=$5,color=$6,year_of_manufacture=$7,rental_price_hourly=$8,rental_price_longterm=$9,description=$10,main_img_url=$11,current_location=$12,license_plate=$13,features=$14,allowed_license_types=$15,modified_at=now() WHERE id=$16 RETURNING id";
        await using var c=new NpgsqlCommand(sql, cn, tx);
        Add(c,v.VehicleTypeId,v.FacilityId,v.Status,v.Brand,v.Model,v.Color,v.YearOfManufacture,v.HourlyPrice,v.LongtermPrice,v.Description,v.MainImageUrl,v.Location,v.LicensePlate,v.Features.ToArray(),Car_Rental_System_BE.Application.Validators.LicenseTypesAttribute.Normalize(v.AllowedLicenseTypes).ToArray());
        if(id is not null)c.Parameters.AddWithValue(id.Value);
        var result=await c.ExecuteScalarAsync(ct);
        if(result is not int vehicleId)throw new KeyNotFoundException();
        await CleanVehicleDetailsAsync(cn, tx, vehicleId, category.Type, category.Fuel, ct);
        if(v.Detail is not null) await SaveVehicleDetailAsync(cn, tx, vehicleId, category.Type, category.Fuel, v.Detail, ct);
        await tx.CommitAsync(ct);
        return (await GetVehicleAsync(vehicleId,ct))!;
    }

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
    }

    private async Task SaveVehicleDetailAsync(NpgsqlConnection cn, NpgsqlTransaction tx, int vehicleId, string type, string fuel, VehicleDetail d, CancellationToken ct)
    {
        if (type == "car")
        {
            await using var cmd = new NpgsqlCommand("INSERT INTO car_detail(vehicle_id,seat_count,transmission,trunk_capacity) VALUES($1,$2,$3,$4) ON CONFLICT(vehicle_id) DO UPDATE SET seat_count=$2,transmission=$3,trunk_capacity=$4", cn, tx);
            Add(cmd, vehicleId, d.SeatCount, d.Transmission, d.TrunkCapacity);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        if (type == "motorbike")
        {
            await using var cmd = new NpgsqlCommand("INSERT INTO motorbike_detail(vehicle_id,bike_type,has_storage_box) VALUES($1,$2,$3) ON CONFLICT(vehicle_id) DO UPDATE SET bike_type=$2,has_storage_box=$3", cn, tx);
            Add(cmd, vehicleId, d.BikeType, d.HasStorageBox);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        if (fuel == "electric")
        {
            await using var cmd = new NpgsqlCommand($"INSERT INTO {ElectricDetailTable(type)}(vehicle_id,battery_capacity,charging_type,charging_time_hours,max_range_km,battery_type) VALUES($1,$2,$3,$4,$5,$6) ON CONFLICT(vehicle_id) DO UPDATE SET battery_capacity=$2,charging_type=$3,charging_time_hours=$4,max_range_km=$5,battery_type=$6", cn, tx);
            Add(cmd, vehicleId, d.BatteryCapacity, d.ChargingType, d.ChargingTimeHours, d.MaxRangeKm, d.BatteryType);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        if (d.GalleryImages is not null)
        {
            await using var del = new NpgsqlCommand("DELETE FROM vehicle_imgs WHERE vehicle_id=$1", cn, tx);
            del.Parameters.AddWithValue(vehicleId);
            await del.ExecuteNonQueryAsync(ct);
            foreach (var img in d.GalleryImages)
            {
                if (string.IsNullOrWhiteSpace(img)) continue;
                await using var ins = new NpgsqlCommand("INSERT INTO vehicle_imgs(vehicle_id,url) VALUES($1,$2)", cn, tx);
                Add(ins, vehicleId, img);
                await ins.ExecuteNonQueryAsync(ct);
            }
        }
    }
    private async Task<VehicleDetail?> LoadVehicleDetailAsync(int vehicleId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        var category = await (from v in db.Set<VehiclesRow>().AsNoTracking()
            join t in db.Set<VehicleTypesRow>() on v.VehicleTypeId equals t.Id
            where v.Id == vehicleId
            select new { Type = t.Type!.Trim().ToLower(), Fuel = t.FuelType!.Trim().ToLower() }).FirstOrDefaultAsync(ct);
        var car = category?.Type == "car" ? await db.Set<CarDetailRow>().AsNoTracking().FirstOrDefaultAsync(x => x.VehicleId == vehicleId, ct) : null;
        var bike = category?.Type == "motorbike" ? await db.Set<MotorbikeDetailRow>().AsNoTracking().FirstOrDefaultAsync(x => x.VehicleId == vehicleId, ct) : null;
        string? battery = null, chargingType = null, chargingHours = null, batteryType = null;
        decimal? range = null;
        if (category?.Fuel == "electric" && category.Type == "car")
        {
            var electric = await db.Set<ElectricCarDetailRow>().AsNoTracking().FirstOrDefaultAsync(x => x.VehicleId == vehicleId, ct);
            if (electric != null) { battery = electric.BatteryCapacity; chargingType = electric.ChargingType; chargingHours = electric.ChargingTimeHours; range = electric.MaxRangeKm; batteryType = electric.BatteryType; }
        }
        else if (category?.Fuel == "electric" && category.Type == "motorbike")
        {
            var electric = await db.Set<ElectricMotorbikeDetailRow>().AsNoTracking().FirstOrDefaultAsync(x => x.VehicleId == vehicleId, ct);
            if (electric != null) { battery = electric.BatteryCapacity; chargingType = electric.ChargingType; chargingHours = electric.ChargingTimeHours; range = electric.MaxRangeKm; batteryType = electric.BatteryType; }
        }
        var images = await db.Set<VehicleImgsRow>().AsNoTracking().Where(x => x.VehicleId == vehicleId).OrderBy(x => x.Id).Select(x => x.Url).ToListAsync(ct);
        var hasAny = car?.SeatCount != null || car?.Transmission != null || car?.TrunkCapacity != null ||
            bike?.BikeType != null || bike?.HasStorageBox != null || battery != null || chargingType != null ||
            chargingHours != null || range != null || batteryType != null || images.Count > 0;
        return hasAny ? new(car?.SeatCount, car?.Transmission, car?.TrunkCapacity, bike?.BikeType, bike?.HasStorageBox,
            battery, chargingType, chargingHours, range, batteryType, images) : null;
    }
    private static string? S(NpgsqlDataReader r,int i)=>r.IsDBNull(i)?null:r.GetString(i);
    private static void Add(NpgsqlCommand c,params object?[] v){foreach(var x in v)c.Parameters.AddWithValue(x??DBNull.Value);}
    private static async Task<object?> ScalarAsync(NpgsqlConnection c,NpgsqlTransaction t,string sql,object? value,CancellationToken ct)=>await ScalarAsync(c,t,sql,new[]{value},ct);private static async Task<object?> ScalarAsync(NpgsqlConnection c,NpgsqlTransaction t,string sql,object?[] values,CancellationToken ct){await using var x=new NpgsqlCommand(sql,c,t);Add(x,values);return await x.ExecuteScalarAsync(ct);}

    // Fleet keeps its own copy of the dispatcher lock: the advisory key is transaction scoped and
    // shared by every writer that touches vehicle availability, so the lock and its key stay as they
    // were under the legacy operations repository.
    // One transaction-scoped dispatcher lock protects cross-table availability invariants.
    // Booking, inspections, shifts, incidents, maintenance and replacements share this lock.
    private static async Task LockOperationsAsync(NpgsqlConnection cn, NpgsqlTransaction tx, CancellationToken ct)
        => await ValueAsync(cn, tx, "SELECT pg_advisory_xact_lock(724232001::bigint)", ct);

    private static async Task<object?> ValueAsync(NpgsqlConnection cn, NpgsqlTransaction tx, string sql, CancellationToken ct, params object?[] values)
    {
        await using var command = new NpgsqlCommand(sql, cn, tx);
        Add(command, values);
        return await command.ExecuteScalarAsync(ct);
    }
}
