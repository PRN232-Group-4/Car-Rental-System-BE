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
        // Khóa ngoại chặn việc xóa; báo xung đột nghiệp vụ thay vì lỗi ràng buộc.
        if(await db.Set<VehiclesRow>().AnyAsync(v => v.VehicleTypeId == id, ct))
            throw new ConflictException("VEHICLE_TYPE_IN_USE", "Loại phương tiện đang được dùng cho xe trong đội. Hãy chuyển các xe sang loại khác trước khi xóa.");
        if(await db.Set<RentalPoliciesRow>().AnyAsync(p => p.VehicleTypeId == id, ct))
            throw new ConflictException("VEHICLE_TYPE_IN_USE", "Loại phương tiện đang được dùng trong chính sách thuê. Hãy cập nhật chính sách trước khi xóa.");
        if(await db.Set<VehicleTypesRow>().Where(t => t.Id == id).ExecuteDeleteAsync(ct)!=1)throw new KeyNotFoundException();
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
        await using var db = VehicleRentalDbContext.Create(cn, tx);
        await LockOperationsAsync(db, ct);
        if (id is not null && await db.LockByKeyAsync<VehiclesRow>(id.Value, ct) is null)
            throw new KeyNotFoundException("Vehicle was not found.");
        var category = await GetVehicleCategoryAsync(db, v.VehicleTypeId, ct);
        ValidateVehicleDetail(category.Type, category.Fuel, v.Detail);
        if(v.Features is null || v.Features.Count>100 || v.Features.Any(x=>string.IsNullOrWhiteSpace(x) || x.Length>100))
            throw new ArgumentException("Features must have at most 100 non-empty values of up to 100 characters.");
        if (v.FacilityId is not null && !await db.Set<FacilitiesRow>().AnyAsync(f => f.Id == v.FacilityId, ct))
            throw new ArgumentException("Facility was not found.");
        var features = v.Features.ToArray();
        var licenseTypes = LicenseTypesAttribute.Normalize(v.AllowedLicenseTypes).ToArray();
        int vehicleId;
        if (id is null)
        {
            var row = new VehiclesRow
            {
                VehicleTypeId = v.VehicleTypeId, FacilityId = v.FacilityId, Status = v.Status, Brand = v.Brand, Model = v.Model,
                Color = v.Color, YearOfManufacture = v.YearOfManufacture, RentalPriceHourly = v.HourlyPrice,
                RentalPriceLongterm = v.LongtermPrice, Description = v.Description, MainImgUrl = v.MainImageUrl,
                CurrentLocation = v.Location, LicensePlate = v.LicensePlate, Features = features, AllowedLicenseTypes = licenseTypes,
            };
            await db.InsertAsync(row, ct);
            vehicleId = row.Id;
        }
        else
        {
            var now = await db.DatabaseNowAsync(ct);
            await db.Set<VehiclesRow>().Where(x => x.Id == id).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.VehicleTypeId, v.VehicleTypeId)
                .SetProperty(x => x.FacilityId, v.FacilityId)
                .SetProperty(x => x.Status, v.Status)
                .SetProperty(x => x.Brand, v.Brand)
                .SetProperty(x => x.Model, v.Model)
                .SetProperty(x => x.Color, v.Color)
                .SetProperty(x => x.YearOfManufacture, v.YearOfManufacture)
                .SetProperty(x => x.RentalPriceHourly, v.HourlyPrice)
                .SetProperty(x => x.RentalPriceLongterm, v.LongtermPrice)
                .SetProperty(x => x.Description, v.Description)
                .SetProperty(x => x.MainImgUrl, v.MainImageUrl)
                .SetProperty(x => x.CurrentLocation, v.Location)
                .SetProperty(x => x.LicensePlate, v.LicensePlate)
                .SetProperty(x => x.Features, features)
                .SetProperty(x => x.AllowedLicenseTypes, licenseTypes)
                .SetProperty(x => x.ModifiedAt, now), ct);
            vehicleId = id.Value;
        }
        await CleanVehicleDetailsAsync(db, vehicleId, category.Type, category.Fuel, ct);
        if(v.Detail is not null) await SaveVehicleDetailAsync(db, vehicleId, category.Type, category.Fuel, v.Detail, ct);
        await tx.CommitAsync(ct);
        return (await GetVehicleAsync(vehicleId,ct))!;
    }

    private static string ElectricDetailTable(string type) => type == "car" ? "electric_car_detail" : "electric_motorbike_detail";

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

    private static async Task SaveVehicleDetailAsync(VehicleRentalDbContext db, int vehicleId, string type, string fuel, VehicleDetail d, CancellationToken ct)
    {
        if (type == "car")
            await UpsertDetailAsync(db, new CarDetailRow { VehicleId = vehicleId, SeatCount = d.SeatCount, Transmission = d.Transmission, TrunkCapacity = d.TrunkCapacity }, ct);
        if (type == "motorbike")
            await UpsertDetailAsync(db, new MotorbikeDetailRow { VehicleId = vehicleId, BikeType = d.BikeType, HasStorageBox = d.HasStorageBox }, ct);
        if (fuel == "electric" && type == "car")
            await UpsertDetailAsync(db, new ElectricCarDetailRow { VehicleId = vehicleId, BatteryCapacity = d.BatteryCapacity, ChargingType = d.ChargingType, ChargingTimeHours = d.ChargingTimeHours, MaxRangeKm = d.MaxRangeKm, BatteryType = d.BatteryType }, ct);
        else if (fuel == "electric")
            await UpsertDetailAsync(db, new ElectricMotorbikeDetailRow { VehicleId = vehicleId, BatteryCapacity = d.BatteryCapacity, ChargingType = d.ChargingType, ChargingTimeHours = d.ChargingTimeHours, MaxRangeKm = d.MaxRangeKm, BatteryType = d.BatteryType }, ct);
        if (d.GalleryImages is not null)
        {
            await db.Set<VehicleImgsRow>().Where(x => x.VehicleId == vehicleId).ExecuteDeleteAsync(ct);
            foreach (var img in d.GalleryImages.Where(img => !string.IsNullOrWhiteSpace(img)))
                db.Add(new VehicleImgsRow { VehicleId = vehicleId, Url = img });
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
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

    // Fleet giữ bản sao riêng của khóa điều phối: advisory key nằm trong phạm vi transaction và dùng
    // chung cho mọi luồng ghi đụng tới tình trạng trống của xe, nên khóa và key giữ nguyên như dưới
    // repository vận hành cũ. Một khóa điều phối trong phạm vi transaction bảo vệ các bất biến về
    // tình trạng trống giữa nhiều bảng. Đặt xe, kiểm tra xe, sự cố, bảo trì và thay xe dùng chung
    // khóa này.
    private static Task LockOperationsAsync(VehicleRentalDbContext db, CancellationToken ct)
        => db.AdvisoryLockAsync(724232001L, ct);
    private static async Task UpsertDetailAsync<T>(VehicleRentalDbContext db, T row, CancellationToken ct) where T : class
    {
        // Dòng xe đã bị khóa (hoặc là xe mới) trong transaction này, nên tìm-rồi-ghi thay cho ON CONFLICT(vehicle_id).
        // UPDATE không khớp dòng nào không phải lỗi PostgreSQL, nên transaction vẫn dùng được cho lệnh INSERT.
        db.Update(row);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            db.Add(row);
            await db.SaveChangesAsync(ct);
        }
        finally { db.ChangeTracker.Clear(); }
    }
}
