using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.DTOs.Organization;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Car_Rental_System_BE.Domain.Availability;
using Car_Rental_System_BE.Infrastructure.Availability;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using Car_Rental_System_BE.Infrastructure.Persistence;

namespace Car_Rental_System_BE.Infrastructure.Repositories;

public sealed partial class NpgsqlOperationsRepository(NpgsqlDataSource dataSource, TimeProvider? timeProvider = null) : IOperationsRepository
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    public Task<IReadOnlyList<OrganizationUnit>> ListDepartmentsAsync(CancellationToken ct)=>UnitsAsync("departments",false,ct);
    public Task<OrganizationUnit> SaveDepartmentAsync(int? id,string name,CancellationToken ct)=>SaveUnitAsync("departments",id,name,null,ct);
    public Task DeleteDepartmentAsync(int id,CancellationToken ct)=>DeleteAsync("departments",id,ct);
    public async Task<OperationsPage<OrganizationUnit>> ListFacilitiesAsync(int page, int pageSize, CancellationToken ct)
    {
        Car_Rental_System_BE.Application.Lifecycle.RentalQueryValidation.Pagination(page, pageSize);
        await using var db = VehicleRentalDbContext.Create(dataSource);
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        var query = db.Set<FacilitiesRow>().AsNoTracking();
        var total = await query.CountAsync(ct);
        var offset = ((long)page - 1) * pageSize;
        var items = offset > int.MaxValue ? new List<OrganizationUnit>() : await query.OrderByDescending(x => x.Id)
            .Skip((int)offset).Take(pageSize).Select(x => new OrganizationUnit(x.Id, x.Name, x.Address, x.Phone)).ToListAsync(ct);
        await tx.CommitAsync(ct);
        return new(items, page, pageSize, total);
    }
    public async Task<OrganizationUnit> SaveFacilityAsync(int? id,string name,string? address,CancellationToken ct,string? phone=null)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        int key;
        if (id is null)
        {
            var row = new FacilitiesRow { Name = name, Address = address, Phone = phone };
            await db.InsertAsync(row, ct);
            key = row.Id;
        }
        else
        {
            if (await db.Set<FacilitiesRow>().Where(f => f.Id == id).ExecuteUpdateAsync(s => s
                    .SetProperty(f => f.Name, name)
                    .SetProperty(f => f.Address, address)
                    .SetProperty(f => f.Phone, f => phone ?? f.Phone), ct) == 0)
                throw new KeyNotFoundException("Facility was not found.");
            key = id.Value;
        }
        return await db.Set<FacilitiesRow>().Where(f => f.Id == key)
            .Select(f => new OrganizationUnit(f.Id, f.Name, f.Address, f.Phone)).SingleAsync(ct);
    }
    public Task DeleteFacilityAsync(int id,CancellationToken ct)=>DeleteAsync("facilities",id,ct);
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
        // FKs restrict the delete; report a business conflict instead of a constraint error.
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
        var licenseTypes = Car_Rental_System_BE.Application.Validators.LicenseTypesAttribute.Normalize(v.AllowedLicenseTypes).ToArray();
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
    public async Task<Rental> CreateRentalAsync(string email,int vehicleId,int? policyId,string type,DateTime start,DateTime end,bool subscription,int? subscriptionId,bool hasDriver,CancellationToken ct,string paymentMethod="payos",DateTime? expireAt=null)
    {
        if(paymentMethod is not ("payos" or "cash"))throw new ArgumentException("PaymentMethod must be payos or cash.");
        if(end<=start)throw new ArgumentException("End time must be after start time.");
        await using var cn=await dataSource.OpenConnectionAsync(ct);
        await using var tx=await cn.BeginTransactionAsync(ct);
        await VehicleLock.AcquireAsync(cn, tx, vehicleId, ct);
        await using var db = VehicleRentalDbContext.Create(cn, tx);
        var customer = await (from c in db.Set<CustomersRow>() join u in db.Set<UsersRow>() on c.UserId equals u.Id
            where u.Email == email select (int?)c.Id).FirstOrDefaultAsync(ct);
        if(customer is not int cid)throw new KeyNotFoundException();
        if(subscription){
            var needed=(decimal)(end-start).TotalHours;
            var today = await db.DatabaseTodayAsync(ct);
            var sub = subscriptionId is int sid ? await db.LockByKeyAsync<CustomerSubscriptionsRow>(sid, ct) : null;
            if (sub is null || sub.CustomerId != cid || sub.Status != "active" || sub.StartDate > today || sub.EndDate < today)
                throw new KeyNotFoundException("Active subscription was not found.");
            var remaining=sub.RemainingHours;
            if(remaining is not null&&remaining<needed)throw new InvalidOperationException("Subscription does not have enough remaining hours.");
            if(remaining is not null)
                await db.Set<CustomerSubscriptionsRow>().Where(x => x.Id == sub.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.RemainingHours, x => x.RemainingHours - needed), ct);
        }
        if(!hasDriver){
            // Lock customer so license submissions/reviews cannot race this authorization.
            await db.LockByKeyAsync<CustomersRow>(cid, ct, RowLock.Share);
            if(!await CustomerLicenseEligibility.IsEligibleAsync(db, cid, vehicleId, end, ct))
                throw new ConflictException("CUSTOMER_LICENSE_NOT_VALID", "Cần GPLX đã duyệt, đúng hạng xe và còn hạn đến hết chuyến thuê.");
        }
        if (!await db.Set<VehiclesRow>().AnyAsync(v => v.Id == vehicleId && v.Status == "available" &&
            !db.Set<RentalsRow>().Any(r => r.VehicleId == v.Id && (r.Status == "renting" || r.Status == "post_inspection")), ct))
            throw new ConflictException("VEHICLE_UNAVAILABLE", "Xe đang được thuê hoặc chưa sẵn sàng bàn giao. Vui lòng chọn xe khác.");
        var availability = await VehicleAvailabilityChecks.EvaluateAsync(cn, tx, vehicleId, new TimeRange(start, end), null, 60, ct);
        VehicleAvailabilityChecks.ThrowIfBlocked(availability, vehicleId);
        var price = await db.Set<VehiclesRow>().Where(v => v.Id == vehicleId)
            .Select(v => type == "hourly" ? v.RentalPriceHourly : v.RentalPriceLongterm).FirstOrDefaultAsync(ct);
        if(price is not decimal unit)throw new KeyNotFoundException();
        var hours=(decimal)(end-start).TotalHours;
        var total=type=="hourly"?unit*hours:unit*Math.Max(1,Math.Ceiling(hours/(24*30)));
        decimal vat=0,deposit=0;
        if(policyId is null){
            policyId = await (from p in db.Set<RentalPoliciesRow>() from v in db.Set<VehiclesRow>()
                where v.Id == vehicleId && p.IsActive && (p.VehicleTypeId == v.VehicleTypeId || p.VehicleTypeId == null)
                orderby p.VehicleTypeId != null descending, p.Id descending select (int?)p.Id).FirstOrDefaultAsync(ct);
        }
        if(policyId is not null){
            var policy = await db.Set<RentalPoliciesRow>().Where(p => p.Id == policyId && p.IsActive)
                .Select(p => new { p.VatRate, p.DepositPercentage }).FirstOrDefaultAsync(ct);
            if (policy != null) { vat = total * policy.VatRate / 100; deposit = total * policy.DepositPercentage / 100; }
        }
        var expiresAt = expireAt is { } requested ? Sql.Ts(requested) : (await db.DatabaseNowAsync(ct)).AddHours(1);
        var rentalRow = new RentalsRow
        {
            CustomerId = cid, VehicleId = vehicleId, PolicyId = policyId, CustomerSubscriptionId = subscriptionId,
            IsSubscription = subscription, RentalType = type, Status = "pending", VatPrice = vat, DepositPrice = deposit,
            TotalPrice = total + vat, ExpireAt = expiresAt,
        };
        await db.InsertAsync(rentalRow, ct);
        var rid = rentalRow.Id;
        try
        {
            if(type=="hourly")
                await db.InsertAsync(new HourRentalDetailRow { RentalId = rid, StartTime = Sql.Ts(start), EndTime = Sql.Ts(end), HasDriver = hasDriver, HourlyPriceSnapshot = unit }, ct);
            else
                await db.InsertAsync(new LongtermRentalContractRow { RentalId = rid, StartTime = Sql.Ts(start), EndTime = Sql.Ts(end), MonthlyPriceSnapshot = unit, HasDriver = hasDriver }, ct);
        }
        catch (Exception ex) when (ex.SqlState() == PostgresErrorCodes.ExclusionViolation)
        {
            throw new ConflictException("VEHICLE_UNAVAILABLE", $"Vehicle {vehicleId} is unavailable for the requested period (exclusion violation).");
        }
        if(!subscription)
            await db.InsertAsync(new InvoicesRow
            {
                RentalId = rid, InvoiceNumber = $"INV-{_time.GetUtcNow():yyyyMMdd}-{rid}", Amount = total + vat + deposit,
                VatAmount = vat, PaymentMethod = paymentMethod,
            }, ct);
        await tx.CommitAsync(ct);
        return (await GetRentalAsync(rid,ct))!;
    }
    public async Task<Rental?> GetRentalAsync(int id, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await RentalQuery(db, db.Set<RentalsRow>().Where(x => x.Id == id)).FirstOrDefaultAsync(ct);
    }

    private static IQueryable<Rental> RentalQuery(VehicleRentalDbContext db, IQueryable<RentalsRow> rentals) =>
        from r in rentals.AsNoTracking()
        join h in db.Set<HourRentalDetailRow>() on r.Id equals h.RentalId into hourly
        from h in hourly.DefaultIfEmpty()
        join l in db.Set<LongtermRentalContractRow>() on r.Id equals l.RentalId into longterm
        from l in longterm.DefaultIfEmpty()
        select new Rental(r.Id, r.CustomerId, r.VehicleId, r.RentalType, r.Status, r.IsSubscription,
            r.VatPrice, r.DepositPrice, r.OvertimePrice, r.TotalPrice,
            (DateTime?)h.StartTime ?? l.StartTime, (DateTime?)h.EndTime ?? l.EndTime,
            h.HourlyPriceSnapshot, l.MonthlyPriceSnapshot, (bool?)h.HasDriver ?? (bool?)l.HasDriver ?? false,
            l.ContractLink == "/api/rentals/" + r.Id + "/contract/document"
                ? "/api/rental/" + r.Id + "/contract/document" : l.ContractLink, l.SignedAt,
            db.Set<RentalOrdersRow>().Where(o => o.RentalId == r.Id).Select(o => (int?)o.Id).FirstOrDefault(),
            db.Set<InspectionsRow>().Any(i => i.RentalId == r.Id && i.VehicleId == r.VehicleId && i.InspectionType == "pre_rental" && i.IsOk == true),
            db.Set<InspectionsRow>().Any(i => i.RentalId == r.Id && i.VehicleId == r.VehicleId && i.InspectionType == "post_rental"));

    public async Task<IReadOnlyList<DriverScheduleEntry>> ListDriverScheduleAsync(int? driver, DateTime from, DateTime to, CancellationToken ct)
    {
        from = NpgsqlVehicleSearchRepository.Timestamp(from);
        to = NpgsqlVehicleSearchRepository.Timestamp(to);
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await (from a in db.Set<VehicleAssignmentsRow>().AsNoTracking()
            join v in db.Set<VehiclesRow>() on a.VehicleId equals v.Id
            join r in db.Set<RentalsRow>() on a.RentalId equals r.Id
            join st in db.Set<StaffsRow>() on a.DriverStaffId equals st.Id
            join u in db.Set<UsersRow>() on st.UserId equals u.Id
            join h in db.Set<HourRentalDetailRow>() on r.Id equals h.RentalId into hourly
            from h in hourly.DefaultIfEmpty()
            join l in db.Set<LongtermRentalContractRow>() on r.Id equals l.RentalId into longterm
            from l in longterm.DefaultIfEmpty()
            let start = (DateTime?)h.StartTime ?? l.StartTime
            let end = (DateTime?)h.EndTime ?? l.EndTime
            where a.Status == "assigned" && r.Status != "cancelled" && r.Status != "completed" && r.Status != "expired" &&
                (driver == null || a.DriverStaffId == driver) && start < to && end > @from
            orderby start, a.Id
            select new DriverScheduleEntry(a.Id, a.RentalId, a.VehicleId, a.DriverStaffId!.Value,
                u.Fullname ?? u.Email, start!.Value, end!.Value, r.Status,
                v.Brand == null ? (v.Model ?? "") : v.Model == null ? v.Brand : v.Brand + " " + v.Model, v.LicensePlate, a.BufferMinutes)).ToListAsync(ct);
    }
    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(bool active, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await PlanQuery(db.Set<SubscriptionPlansRow>().Where(x => !active || x.IsActive).OrderBy(x => x.Id)).ToListAsync(ct);
    }
    public async Task<SubscriptionPlan?> GetPlanAsync(int id, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await PlanQuery(db.Set<SubscriptionPlansRow>().Where(x => x.Id == id)).FirstOrDefaultAsync(ct);
    }
    private static IQueryable<SubscriptionPlan> PlanQuery(IQueryable<SubscriptionPlansRow> plans) =>
        plans.AsNoTracking().Select(x => new SubscriptionPlan(x.Id, x.Name,
            x.Description, x.Price, x.DurationDays, x.IncludedHours, x.IsActive));
    public async Task DeletePlanAsync(int id,CancellationToken ct)
    {
        await using var cn=await dataSource.OpenConnectionAsync(ct);
        await using var tx=await cn.BeginTransactionAsync(ct);
        await using var db = VehicleRentalDbContext.Create(cn, tx);
        // Plans referenced by subscriptions or payments (FK restricted) are retired instead of deleted.
        var used = await db.Set<CustomerSubscriptionsRow>().AnyAsync(x => x.PlanId == id, ct)
            || await db.Set<PaymentsRow>().AnyAsync(x => x.PlanId == id, ct);
        var plans = db.Set<SubscriptionPlansRow>().Where(x => x.Id == id);
        var affected = used
            ? await plans.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false), ct)
            : await plans.ExecuteDeleteAsync(ct);
        if(affected!=1)throw new KeyNotFoundException();
        await tx.CommitAsync(ct);
    }
    public async Task<SubscriptionPlan> SavePlanAsync(int? id,SubscriptionPlan x,CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        int key;
        if (id is null)
        {
            var row = new SubscriptionPlansRow { Name = x.Name, Description = x.Description, Price = x.Price, DurationDays = x.DurationDays, IncludedHours = x.IncludedHours, IsActive = x.IsActive };
            await db.InsertAsync(row, ct);
            key = row.Id;
        }
        else
        {
            if (await db.Set<SubscriptionPlansRow>().Where(p => p.Id == id).ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.Name, x.Name)
                    .SetProperty(p => p.Description, x.Description)
                    .SetProperty(p => p.Price, x.Price)
                    .SetProperty(p => p.DurationDays, x.DurationDays)
                    .SetProperty(p => p.IncludedHours, x.IncludedHours)
                    .SetProperty(p => p.IsActive, x.IsActive), ct) == 0)
                throw new KeyNotFoundException();
            key = id.Value;
        }
        return (await ListPlansAsync(false,ct)).Single(p=>p.Id==key);
    }
    public async Task<int> BuyPlanAsync(string email,int plan,CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        var customer = await (from c in db.Set<CustomersRow>() join u in db.Set<UsersRow>() on c.UserId equals u.Id
            where u.Email == email select (int?)c.Id).FirstOrDefaultAsync(ct);
        var selected = await db.Set<SubscriptionPlansRow>().Where(p => p.Id == plan && p.IsActive)
            .Select(p => new { p.Id, p.DurationDays, p.IncludedHours }).FirstOrDefaultAsync(ct);
        if (customer is null || selected is null) throw new KeyNotFoundException();
        var today = await db.DatabaseTodayAsync(ct);
        var row = new CustomerSubscriptionsRow
        {
            CustomerId = customer.Value, PlanId = selected.Id, StartDate = today, EndDate = today.AddDays(selected.DurationDays),
            RemainingHours = selected.IncludedHours,
        };
        await db.InsertAsync(row, ct);
        return row.Id;
    }
    public async Task<IReadOnlyList<CustomerSubscription>> ListCustomerSubscriptionsAsync(string email, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await (from cs in db.Set<CustomerSubscriptionsRow>().AsNoTracking()
            join cu in db.Set<CustomersRow>() on cs.CustomerId equals cu.Id
            join u in db.Set<UsersRow>() on cu.UserId equals u.Id
            join p in db.Set<SubscriptionPlansRow>() on cs.PlanId equals p.Id
            where u.Email == email
            orderby cs.Id descending
            select new CustomerSubscription(cs.Id, cs.CustomerId, cs.PlanId, p.Name,
                cs.StartDate, cs.EndDate, p.IncludedHours, cs.RemainingHours, cs.Status)).ToListAsync(ct);
    }
    public async Task<int> ExpireSubscriptionsAsync(DateOnly today,CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await db.Set<CustomerSubscriptionsRow>().Where(x => x.Status == "active" && x.EndDate < today)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "expired"), ct);
    }
    public async Task<IReadOnlyList<ExpiringSubscription>> ListSubscriptionsToRemindAsync(DateOnly today, int withinDays, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        var through = today.AddDays(withinDays);
        return await (from cs in db.Set<CustomerSubscriptionsRow>().AsNoTracking()
            join cu in db.Set<CustomersRow>() on cs.CustomerId equals cu.Id
            join u in db.Set<UsersRow>() on cu.UserId equals u.Id
            where cs.Status == "active" && cs.ReminderSentAt == null && cs.EndDate >= today && cs.EndDate <= through
            orderby cs.EndDate
            select new ExpiringSubscription(cs.Id, u.Email, u.Fullname ?? u.Email, cs.EndDate, cs.RemainingHours)).ToListAsync(ct);
    }
    public async Task MarkSubscriptionRemindedAsync(int subscriptionId,CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        var now = await db.DatabaseNowAsync(ct);
        await db.Set<CustomerSubscriptionsRow>().Where(x => x.Id == subscriptionId && x.ReminderSentAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReminderSentAt, now), ct);
    }
    public async Task<Rental?> GetRentalForUserAsync(int id, string email, bool readAll, bool customer, bool driver, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        var rentals = db.Set<RentalsRow>().Where(r => r.Id == id && (readAll ||
            (customer && (from c in db.Set<CustomersRow>() join u in db.Set<UsersRow>() on c.UserId equals u.Id
                where c.Id == r.CustomerId && u.Email == email && u.IsActive select c.Id).Any()) ||
            (driver && (from a in db.Set<VehicleAssignmentsRow>()
                join st in db.Set<StaffsRow>() on a.DriverStaffId equals st.Id
                join u in db.Set<UsersRow>() on st.UserId equals u.Id
                where a.RentalId == r.Id && a.Status == "assigned" && u.Email == email && u.IsActive && st.IsDriver
                select a.Id).Any())));
        return await RentalQuery(db, rentals).FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<Maintenance>> ListMaintenanceAsync(int? vehicleId, string? status, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await db.Set<VehicleMaintenanceRow>().AsNoTracking()
            .Where(x => (vehicleId == null || x.VehicleId == vehicleId) && (status == null || x.Status == status))
            .OrderByDescending(x => x.Id).Select(x => new Maintenance(x.Id, x.VehicleId, x.FacilityId,
                x.AssignedStaffId, x.Status, x.MaintenanceType, x.ScheduledDate, x.IsSafe, x.Description, x.Cost,
                null, null, null, null)).ToListAsync(ct);
    }
    private async Task<IReadOnlyList<OrganizationUnit>> UnitsAsync(string table, bool address, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        if (table == "departments") return await db.Set<DepartmentsRow>().AsNoTracking().OrderBy(x => x.Name)
            .Select(x => new OrganizationUnit(x.Id, x.Name, null, null)).ToListAsync(ct);
        return await db.Set<FacilitiesRow>().AsNoTracking().OrderBy(x => x.Name)
            .Select(x => new OrganizationUnit(x.Id, x.Name, address ? x.Address : null, null)).ToListAsync(ct);
    }

    private async Task<OrganizationUnit> SaveUnitAsync(string table,int? id,string name,string? address,CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        if (table == "facilities")
        {
            int key;
            if (id is null) { var row = new FacilitiesRow { Name = name, Address = address }; await db.InsertAsync(row, ct); key = row.Id; }
            else if (await db.Set<FacilitiesRow>().Where(f => f.Id == id).ExecuteUpdateAsync(s => s
                         .SetProperty(f => f.Name, name).SetProperty(f => f.Address, address), ct) == 0) throw new KeyNotFoundException();
            else key = id.Value;
            return await db.Set<FacilitiesRow>().Where(f => f.Id == key).Select(f => new OrganizationUnit(f.Id, f.Name, f.Address, null)).SingleAsync(ct);
        }
        if (id is null) { var row = new DepartmentsRow { Name = name }; await db.InsertAsync(row, ct); return new(row.Id, name, null); }
        if (await db.Set<DepartmentsRow>().Where(d => d.Id == id).ExecuteUpdateAsync(s => s.SetProperty(d => d.Name, name), ct) == 0)
            throw new KeyNotFoundException();
        return new(id.Value, name, null);
    }
    private async Task DeleteAsync(string table,int id,CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        int deleted;
        try
        {
            deleted = table == "facilities"
                ? await db.Set<FacilitiesRow>().Where(f => f.Id == id).ExecuteDeleteAsync(ct)
                : await db.Set<DepartmentsRow>().Where(d => d.Id == id).ExecuteDeleteAsync(ct);
        }
        catch (Exception ex) when (ex.SqlState() == PostgresErrorCodes.ForeignKeyViolation)
        {
            throw new ConflictException(table == "facilities" ? "FACILITY_IN_USE" : "DEPARTMENT_IN_USE",
                "Bản ghi đang được tham chiếu bởi dữ liệu khác nên không thể xóa.");
        }
        if(deleted!=1)throw new KeyNotFoundException();
    }
    private static async Task UpsertDetailAsync<T>(VehicleRentalDbContext db, T row, CancellationToken ct) where T : class
    {
        // The vehicle row is locked (or new) in this transaction, so find-then-write replaces ON CONFLICT(vehicle_id).
        // An UPDATE that matches no row is not a PostgreSQL error, so the transaction stays usable for the INSERT.
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
    public async Task<StaffRating> RateStaffAsync(int rentalId, int staffId, decimal rating, string? comment, string customerEmail, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        if (!await (from rental in db.Set<RentalsRow>()
            join c in db.Set<CustomersRow>() on rental.CustomerId equals c.Id
            join u in db.Set<UsersRow>() on c.UserId equals u.Id
            where rental.Id == rentalId && u.Email == customerEmail && rental.Status == "completed"
            select rental.Id).AnyAsync(ct)) throw new KeyNotFoundException("Rental not found or not completed.");
        if (!await db.Set<VehicleAssignmentsRow>().AnyAsync(x => x.RentalId == rentalId && x.DriverStaffId == staffId, ct) &&
            !await db.Set<InspectionsRow>().AnyAsync(x => x.RentalId == rentalId && x.StaffId == staffId, ct))
            throw new KeyNotFoundException("Staff was not involved in this rental.");

        // ON CONFLICT (rental_id, staff_id) DO UPDATE: update first, insert otherwise, retry the update on a lost race.
        if (await UpdateRatingAsync(db, rentalId, staffId, rating, comment, ct) == 0 &&
            !await db.TryInsertAsync(new StaffRatingsRow { RentalId = rentalId, StaffId = staffId, Rating = rating, Comment = comment }, ct))
            await UpdateRatingAsync(db, rentalId, staffId, rating, comment, ct);
        var saved = await db.Set<StaffRatingsRow>().AsNoTracking().SingleAsync(x => x.RentalId == rentalId && x.StaffId == staffId, ct);
        return new(saved.Id, saved.RentalId, saved.StaffId, saved.Rating, saved.Comment, saved.CreatedAt);
    }

    private static Task<int> UpdateRatingAsync(VehicleRentalDbContext db, int rentalId, int staffId, decimal rating, string? comment, CancellationToken ct) =>
        db.Set<StaffRatingsRow>().Where(x => x.RentalId == rentalId && x.StaffId == staffId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Rating, rating).SetProperty(x => x.Comment, comment), ct);

}
