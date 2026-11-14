using Car_Rental_System_BE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Car_Rental_System_BE.Application.Features.Audit;

namespace Car_Rental_System_BE.Infrastructure.Features.Audit;

public sealed class NpgsqlAuditLogRepository(NpgsqlDataSource dataSource) : IAuditLogRepository
{
    public async Task<AuditLogPage> ListAsync(AuditLogQuery query, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        await using var db = VehicleRentalDbContext.Create(connection, transaction);
        var source = from audit in db.Set<UserAdministrationAuditRow>()
            from actor in db.Set<UsersRow>().Where(u => u.Id == audit.ActorUserId).DefaultIfEmpty()
            from target in db.Set<UsersRow>().Where(u => u.Id == audit.UserId).DefaultIfEmpty()
            select new
            {
                audit.Id, audit.CreatedAt, audit.ActorUserId, ActorEmail = actor.Email, ActorName = actor.Fullname,
                Action = audit.Action == "role:add" ? "role_assigned" : audit.Action == "role:remove" ? "role_removed" :
                    audit.Action == "deactivate" ? "account_locked" : audit.Action == "activate" ? "account_unlocked" :
                    audit.Action == "sessions:revoke" ? "session_revoked" : audit.Action,
                audit.UserId, TargetEmail = target.Email, TargetName = target.Fullname,
                audit.Reason, audit.IpAddress, audit.Status
            };
        if (query.Search is { } search)
        {
            var term = search.ToLower();
            source = source.Where(a => (a.ActorEmail ?? "").ToLower().Contains(term) ||
                (a.ActorName ?? "").ToLower().Contains(term) || (a.TargetEmail ?? "").ToLower().Contains(term) ||
                (a.TargetName ?? "").ToLower().Contains(term) || ("AUD-" + a.Id).ToLower().Contains(term) ||
                a.ActorUserId.ToString().Contains(search) || a.UserId.ToString().Contains(search));
        }
        if (query.Action is { } action) source = source.Where(a => a.Action == action);
        var total = await source.CountAsync(ct);
        var offset = ((long)query.Page - 1) * query.PageSize;
        var rows = offset >= total ? [] : await source.OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .Skip((int)offset).Take(query.PageSize).ToListAsync(ct);
        var items = rows.Select(a => new AuditLogItem($"AUD-{a.Id}", a.CreatedAt, a.ActorUserId,
            a.ActorEmail, a.ActorName, a.Action, a.UserId, a.TargetEmail, a.Reason, a.IpAddress, a.Status)).ToList();
        await transaction.CommitAsync(ct);
        return new(items, query.Page, query.PageSize, total);
    }
}
