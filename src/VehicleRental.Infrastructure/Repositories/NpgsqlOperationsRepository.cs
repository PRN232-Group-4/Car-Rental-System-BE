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
        var sql=id is null?"INSERT INTO facilities(name,address,phone) VALUES($1,$2,$3) RETURNING id,name,address,phone":"UPDATE facilities SET name=$1,address=$2,phone=COALESCE($3,phone) WHERE id=$4 RETURNING id,name,address,phone";
        await using var command=dataSource.CreateCommand(sql);
        Add(command,name,address,phone);
        if(id is not null) Add(command,id);
        await using var reader=await command.ExecuteReaderAsync(ct);
        if(!await reader.ReadAsync(ct)) throw new KeyNotFoundException("Facility was not found.");
        return new(reader.GetInt32(0),reader.GetString(1),S(reader,2),S(reader,3));
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
            await using var sub=new NpgsqlCommand("SELECT remaining_hours FROM customer_subscriptions WHERE id=$1 AND customer_id=$2 AND status='active' AND start_date<=CURRENT_DATE AND end_date>=CURRENT_DATE FOR UPDATE",cn,tx);
            Add(sub,subscriptionId,cid);
            await using var sr=await sub.ExecuteReaderAsync(ct);
            if(!await sr.ReadAsync(ct))throw new KeyNotFoundException("Active subscription was not found.");
            var remaining=sr.IsDBNull(0)?(decimal?)null:sr.GetDecimal(0);
            await sr.CloseAsync();
            if(remaining is not null&&remaining<needed)throw new InvalidOperationException("Subscription does not have enough remaining hours.");
            if(remaining is not null){
                await using var deduct=new NpgsqlCommand("UPDATE customer_subscriptions SET remaining_hours=remaining_hours-$1 WHERE id=$2",cn,tx);
                Add(deduct,needed,subscriptionId);
                await deduct.ExecuteNonQueryAsync(ct);
            }
        }
        if(!hasDriver){
            // Lock customer so license submissions/reviews cannot race this authorization.
            await ScalarAsync(cn,tx,"SELECT id FROM customers WHERE id=$1 FOR SHARE",cid,ct);
            await using var license=new NpgsqlCommand("SELECT " + CustomerLicenseSql.Eligible("$1", "$2", "$3::timestamp::date"),cn,tx);
            Add(license,cid,vehicleId,end);
            if(await license.ExecuteScalarAsync(ct) is not true)
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
        var rentalId=await ScalarAsync(cn,tx,"INSERT INTO rentals(customer_id,vehicle_id,policy_id,customer_subscription_id,is_subscription,rental_type,status,vat_price,deposit_price,total_price,expire_at)VALUES($1,$2,$3,$4,$5,$6,'pending',$7,$8,$9,COALESCE($10::timestamp, now() + interval '1 hour')) RETURNING id",new object?[]{cid,vehicleId,policyId,subscriptionId,subscription,type,vat,deposit,total+vat,expireAt},ct);
        if(rentalId is not int rid)throw new InvalidOperationException();
        try
        {
            if(type=="hourly"){
                await using var d=new NpgsqlCommand("INSERT INTO hour_rental_detail(rental_id,start_time,end_time,has_driver,hourly_price_snapshot)VALUES($1,$2,$3,$4,$5)",cn,tx);
                Add(d,rid,start,end,hasDriver,unit);
                await d.ExecuteNonQueryAsync(ct);
            }else{
                await using var d=new NpgsqlCommand("INSERT INTO longterm_rental_contract(rental_id,start_time,end_time,monthly_price_snapshot,has_driver)VALUES($1,$2,$3,$4,$5)",cn,tx);
                Add(d,rid,start,end,unit,hasDriver);
                await d.ExecuteNonQueryAsync(ct);
            }
        }
        catch (PostgresException ex) when (ex.SqlState == "23P01")
        {
            throw new ConflictException("VEHICLE_UNAVAILABLE", $"Vehicle {vehicleId} is unavailable for the requested period (exclusion violation).");
        }
        if(!subscription){
            await using var inv=new NpgsqlCommand("INSERT INTO invoices(rental_id,invoice_number,amount,vat_amount,payment_method)VALUES($1,$2,$3,$4,$5)",cn,tx);
            Add(inv,rid,$"INV-{_time.GetUtcNow():yyyyMMdd}-{rid}",total+vat+deposit,vat,paymentMethod);
            await inv.ExecuteNonQueryAsync(ct);
        }
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
    public async Task DeletePlanAsync(int id,CancellationToken ct){await using var cn=await dataSource.OpenConnectionAsync(ct);await using var tx=await cn.BeginTransactionAsync(ct);var used=await ScalarAsync(cn,tx,"SELECT EXISTS(SELECT 1 FROM customer_subscriptions WHERE plan_id=$1)",id,ct);await using var c=new NpgsqlCommand(used is true?"UPDATE subscription_plans SET is_active=false WHERE id=$1":"DELETE FROM subscription_plans WHERE id=$1",cn,tx);c.Parameters.AddWithValue(id);if(await c.ExecuteNonQueryAsync(ct)!=1)throw new KeyNotFoundException();await tx.CommitAsync(ct);}
    public async Task<SubscriptionPlan> SavePlanAsync(int? id,SubscriptionPlan x,CancellationToken ct){var q=id is null?"INSERT INTO subscription_plans(name,description,price,duration_days,included_hours,is_active)VALUES($1,$2,$3,$4,$5,$6) RETURNING id":"UPDATE subscription_plans SET name=$1,description=$2,price=$3,duration_days=$4,included_hours=$5,is_active=$6 WHERE id=$7 RETURNING id";await using var c=dataSource.CreateCommand(q);Add(c,x.Name,x.Description,x.Price,x.DurationDays,x.IncludedHours,x.IsActive);if(id is not null)c.Parameters.AddWithValue(id.Value);var key=await c.ExecuteScalarAsync(ct);if(key is not int value)throw new KeyNotFoundException();return (await ListPlansAsync(false,ct)).Single(x=>x.Id==value);}
    public async Task<int> BuyPlanAsync(string email,int plan,CancellationToken ct){await using var c=dataSource.CreateCommand("INSERT INTO customer_subscriptions(customer_id,plan_id,start_date,end_date,remaining_hours) SELECT c.id,p.id,CURRENT_DATE,CURRENT_DATE+p.duration_days,p.included_hours FROM customers c CROSS JOIN subscription_plans p JOIN users u ON u.id=c.user_id WHERE u.email=$1 AND p.id=$2 AND p.is_active=true RETURNING id");Add(c,email,plan);return (int?)await c.ExecuteScalarAsync(ct)??throw new KeyNotFoundException();}
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
    public async Task<int> ExpireSubscriptionsAsync(DateOnly today,CancellationToken ct){await using var c=dataSource.CreateCommand("UPDATE customer_subscriptions SET status='expired' WHERE status='active' AND end_date<$1");c.Parameters.AddWithValue(today);return await c.ExecuteNonQueryAsync(ct);}
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
    public async Task MarkSubscriptionRemindedAsync(int subscriptionId,CancellationToken ct){await using var c=dataSource.CreateCommand("UPDATE customer_subscriptions SET reminder_sent_at=now() WHERE id=$1 AND reminder_sent_at IS NULL");c.Parameters.AddWithValue(subscriptionId);await c.ExecuteNonQueryAsync(ct);}
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

    public async Task<object> DashboardAsync(DateTime? from, DateTime? to, CancellationToken ct)
    {
        from = from is { } fromValue ? NpgsqlVehicleSearchRepository.Timestamp(fromValue) : null;
        to = to is { } toValue ? NpgsqlVehicleSearchRepository.Timestamp(toValue) : null;
        await using var db = VehicleRentalDbContext.Create(dataSource);
        var revenue = await db.Set<InvoicesRow>().Where(i => i.Status == "paid" &&
            (from == null || i.IssuedAt >= from) && (to == null || i.IssuedAt <= to)).SumAsync(i => (decimal?)i.Amount, ct) ?? 0;
        var renting = await db.Set<RentalsRow>().LongCountAsync(r => r.Status == "renting", ct);
        var maintenance = await db.Set<VehicleMaintenanceRow>().LongCountAsync(m => m.Status != null && m.Status != "completed" && m.Status != "cancelled", ct);
        var activeSubscriptions = await db.Set<CustomerSubscriptionsRow>().LongCountAsync(s => s.Status == "active" && s.EndDate >= DateOnly.FromDateTime(DateTime.Today), ct);
        return new { revenue, renting, maintenance, activeSubscriptions };
    }
    private async Task<IReadOnlyList<OrganizationUnit>> UnitsAsync(string table, bool address, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        if (table == "departments") return await db.Set<DepartmentsRow>().AsNoTracking().OrderBy(x => x.Name)
            .Select(x => new OrganizationUnit(x.Id, x.Name, null, null)).ToListAsync(ct);
        return await db.Set<FacilitiesRow>().AsNoTracking().OrderBy(x => x.Name)
            .Select(x => new OrganizationUnit(x.Id, x.Name, address ? x.Address : null, null)).ToListAsync(ct);
    }

    private async Task<OrganizationUnit> SaveUnitAsync(string table,int? id,string name,string? address,CancellationToken ct){var q=id is null?(table=="facilities"?"INSERT INTO facilities(name,address)VALUES($1,$2) RETURNING id,name,address":"INSERT INTO departments(name)VALUES($1) RETURNING id,name"):(table=="facilities"?"UPDATE facilities SET name=$1,address=$2 WHERE id=$3 RETURNING id,name,address":"UPDATE departments SET name=$1 WHERE id=$2 RETURNING id,name");await using var c=dataSource.CreateCommand(q);Add(c,name);if(table=="facilities")c.Parameters.AddWithValue((object?)address??DBNull.Value);if(id is not null)c.Parameters.AddWithValue(id.Value);await using var r=await c.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))throw new KeyNotFoundException();return new(r.GetInt32(0),r.GetString(1),r.FieldCount>2?S(r,2):null);}
    private async Task DeleteAsync(string table,int id,CancellationToken ct){await using var c=dataSource.CreateCommand($"DELETE FROM {table} WHERE id=$1");c.Parameters.AddWithValue(id);if(await c.ExecuteNonQueryAsync(ct)!=1)throw new KeyNotFoundException();}
    private static RentalPolicy ReadPolicy(NpgsqlDataReader r)=>new(r.GetInt32(0),r.IsDBNull(1)?null:r.GetInt32(1),r.GetString(2),r.GetDecimal(3),r.GetDecimal(4),r.GetDecimal(5),r.GetBoolean(6));
    private static Rental ReadRental(NpgsqlDataReader r)=>new(r.GetInt32(0),r.GetInt32(1),r.GetInt32(2),r.GetString(3),r.GetString(4),r.GetBoolean(5),r.GetDecimal(6),r.GetDecimal(7),r.GetDecimal(8),r.GetDecimal(9),r.IsDBNull(10)?null:r.GetDateTime(10),r.IsDBNull(11)?null:r.GetDateTime(11),r.IsDBNull(12)?null:r.GetDecimal(12),r.IsDBNull(13)?null:r.GetDecimal(13),r.IsDBNull(14)?null:r.GetBoolean(14),(r.IsDBNull(15)?null:r.GetString(15)) == $"/api/rentals/{r.GetInt32(0)}/contract/document" ? $"/api/rental/{r.GetInt32(0)}/contract/document" : (r.IsDBNull(15)?null:r.GetString(15)),r.IsDBNull(16)?null:r.GetDateTime(16),r.FieldCount>17&&!r.IsDBNull(17)?r.GetInt32(17):null,r.FieldCount>18&&r.GetBoolean(18),r.FieldCount>19&&r.GetBoolean(19));
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
    private static string? S(NpgsqlDataReader r,int i)=>r.IsDBNull(i)?null:r.GetString(i); private static DateOnly? D(NpgsqlDataReader r,int i)=>r.IsDBNull(i)?null:r.GetFieldValue<DateOnly>(i);
    private static void Add(NpgsqlCommand c,params object?[] v){foreach(var x in v)c.Parameters.AddWithValue(x??DBNull.Value);}
    private static async Task<object?> ScalarAsync(NpgsqlConnection c,NpgsqlTransaction t,string sql,object? value,CancellationToken ct)=>await ScalarAsync(c,t,sql,new[]{value},ct);private static async Task<object?> ScalarAsync(NpgsqlConnection c,NpgsqlTransaction t,string sql,object?[] values,CancellationToken ct){await using var x=new NpgsqlCommand(sql,c,t);Add(x,values);return await x.ExecuteScalarAsync(ct);}

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

        await using var cmd = dataSource.CreateCommand("""
            INSERT INTO staff_ratings (rental_id, staff_id, rating, comment)
            VALUES ($1, $2, $3, $4)
            ON CONFLICT (rental_id, staff_id) DO UPDATE SET rating = EXCLUDED.rating, comment = EXCLUDED.comment
            RETURNING id, rental_id, staff_id, rating, comment, created_at
            """);
        Add(cmd, rentalId, staffId, rating, (object?)comment ?? DBNull.Value);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) throw new InvalidOperationException();
        return new(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetDecimal(3), S(r, 4), r.GetDateTime(5));
    }

    public async Task<IReadOnlyList<StaffRating>> ListStaffRatingsAsync(int staffId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await db.Set<StaffRatingsRow>().AsNoTracking().Where(x => x.StaffId == staffId)
            .OrderByDescending(x => x.CreatedAt).Select(x => new StaffRating(x.Id, x.RentalId,
                x.StaffId, x.Rating, x.Comment, x.CreatedAt, null, "visible", null, null, null)).ToListAsync(ct);
    }

}

