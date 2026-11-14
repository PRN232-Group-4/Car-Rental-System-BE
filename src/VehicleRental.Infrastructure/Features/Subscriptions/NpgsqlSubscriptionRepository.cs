using Car_Rental_System_BE.Application.Features.Subscriptions;
using Car_Rental_System_BE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Features.Subscriptions;

public sealed class NpgsqlSubscriptionRepository(NpgsqlDataSource dataSource) : ISubscriptionRepository
{
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
