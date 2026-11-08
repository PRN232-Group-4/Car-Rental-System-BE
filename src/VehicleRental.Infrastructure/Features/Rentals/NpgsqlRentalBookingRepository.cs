using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Features.Rentals;
using Car_Rental_System_BE.Domain.Features.Availability;
using Car_Rental_System_BE.Infrastructure.Features.Availability;
using Car_Rental_System_BE.Infrastructure.Repositories;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using Car_Rental_System_BE.Infrastructure.Persistence;

namespace Car_Rental_System_BE.Infrastructure.Features.Rentals;

/// <summary>
/// Rental booking and rental reads. The members came from <c>NpgsqlOperationsRepository</c> with their SQL, their
/// transaction and their rules unchanged: one transaction under the vehicle lock, subscription hour deduction,
/// the self-drive licence check, the availability window, the price snapshot from the vehicle (hourly or monthly),
/// the automatic policy pick, the VAT/deposit arithmetic, the detail row and the invoice.
/// </summary>
public sealed class NpgsqlRentalBookingRepository(NpgsqlDataSource dataSource, TimeProvider? timeProvider = null) : IRentalBookingRepository
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
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

    // Private copies of the operations repository helpers, byte-identical to the originals: the moved body keeps
    // its exact SQL without widening a legacy type's surface.
    private static void Add(NpgsqlCommand c,params object?[] v){foreach(var x in v)c.Parameters.AddWithValue(x??DBNull.Value);}
    private static async Task<object?> ScalarAsync(NpgsqlConnection c,NpgsqlTransaction t,string sql,object? value,CancellationToken ct)=>await ScalarAsync(c,t,sql,new[]{value},ct);private static async Task<object?> ScalarAsync(NpgsqlConnection c,NpgsqlTransaction t,string sql,object?[] values,CancellationToken ct){await using var x=new NpgsqlCommand(sql,c,t);Add(x,values);return await x.ExecuteScalarAsync(ct);}
}
