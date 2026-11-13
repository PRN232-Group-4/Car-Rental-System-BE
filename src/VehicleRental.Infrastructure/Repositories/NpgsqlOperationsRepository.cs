using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Car_Rental_System_BE.Infrastructure.Features.Availability;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using Car_Rental_System_BE.Infrastructure.Persistence;

namespace Car_Rental_System_BE.Infrastructure.Repositories;

public sealed partial class NpgsqlOperationsRepository(NpgsqlDataSource dataSource) : IOperationsRepository
{
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
}
