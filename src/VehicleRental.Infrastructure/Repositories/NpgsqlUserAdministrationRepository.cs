using Car_Rental_System_BE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Car_Rental_System_BE.Application.DTOs.Accounts;
using Car_Rental_System_BE.Application.Interfaces.External;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Repositories;

public sealed class NpgsqlUserAdministrationRepository(NpgsqlDataSource dataSource, IAuditRequestContext? auditContext = null) : IUserAdministrationRepository
{
    private static readonly string[] StaffSpecialties = ["CustomerService", "Driver", "Maintenance"];

    private static IQueryable<AdministrativeUser> Project(VehicleRentalDbContext db, IQueryable<UsersRow> users) =>
        users.Select(u => new AdministrativeUser(u.Id, u.Email, u.Fullname ?? "", u.Phone, u.Avatar,
            u.Gender, u.Address, u.IsActive, u.CreatedAt,
            (from membership in db.Set<UserRolesRow>() join role in db.Set<RolesRow>() on membership.RoleId equals role.Id
             where membership.UserId == u.Id orderby role.Name select role.Name).Distinct().OrderBy(name => name).ToArray(),
            db.Set<CustomersRow>().Where(c => c.UserId == u.Id).Select(c => (int?)c.Id).SingleOrDefault(),
            db.Set<StaffsRow>().Where(staff => staff.UserId == u.Id).Select(staff => (int?)staff.Id).SingleOrDefault()));

    public async Task<AdministrativeUserPage> ListAsync(UserListQuery query, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        var pattern = "%" + query.Search + "%";
        var users = db.Set<UsersRow>().Where(u =>
            (query.Search == null || EF.Functions.ILike(u.Email, pattern) || EF.Functions.ILike(u.Fullname!, pattern) || EF.Functions.ILike(u.Phone!, pattern))
            && (query.IsActive == null || u.IsActive == query.IsActive)
            && (query.Role == null || (from membership in db.Set<UserRolesRow>()
                join role in db.Set<RolesRow>() on membership.RoleId equals role.Id
                where membership.UserId == u.Id && role.Name.ToLower() == query.Role.ToLower() select role.Id).Any()));
        var total = await users.CountAsync(ct);
        var offset = ((long)query.Page - 1) * query.PageSize;
        var items = offset >= total ? [] : await Project(db, users.OrderByDescending(u => u.Id).Skip((int)offset).Take(query.PageSize)).ToListAsync(ct);
        await transaction.CommitAsync(ct);
        return new(items, query.Page, query.PageSize, total);
    }

    public async Task<AdministrativeUser?> FindAsync(int id, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await Project(db, db.Set<UsersRow>().Where(u => u.Id == id)).SingleOrDefaultAsync(ct);
    }

    public Task<AdministrativeUser> SetStatusAsync(int id, bool isActive, string reason, int actorUserId, CancellationToken ct) =>
        MutateAsync(id, actorUserId, isActive ? "activate" : "deactivate", reason, async db =>
        {
            if (!isActive)
            {
                if (id == actorUserId) throw new UserAdministrationException(409, "You cannot deactivate your own account.");
                await ProtectLastAdminAsync(db, id, ct);
            }
            await db.Set<UsersRow>().Where(u => u.Id == id).ExecuteUpdateAsync(s => s
                .SetProperty(u => u.IsActive, isActive)
                .SetProperty(u => u.AuthorizationVersion, u => u.AuthorizationVersion + 1), ct);
        }, ct);

    public Task<AdministrativeUser> SetRoleAsync(int id, string role, bool assigned, int actorUserId, CancellationToken ct) =>
        MutateAsync(id, actorUserId, assigned ? "role:add" : "role:remove", role, async db =>
        {
            if (assigned && id == actorUserId)
                throw new UserAdministrationException(409, "You cannot assign additional roles to yourself.");
            // Quy về đúng cách viết đã lưu trước khi áp dụng các bất biến dựng sẵn: ví dụ yêu cầu
            // cho 'admin' vẫn phải chạy cơ chế bảo vệ Admin đang hoạt động cuối cùng.
            var requested = role;
            var stored = await db.Set<RolesRow>().Where(r => r.Name.ToLower() == requested.ToLower())
                .Select(r => new { r.Id, r.Name }).FirstOrDefaultAsync(ct)
                ?? throw new UserAdministrationException(400, "Role was not found.");
            role = stored.Name;
            if (assigned) await ValidateRoleProfileAsync(db, id, role, ct);
            if (!assigned && role == "Admin") await ProtectLastAdminAsync(db, id, ct);
            if (!assigned && role == "Staff" && await HasAnyRoleAsync(db, id, StaffSpecialties, ct))
                throw new UserAdministrationException(409, "Remove the staff specialty roles before removing Staff.");
            var name = role;
            if (assigned)
            {
                var roleIds = await db.Set<RolesRow>().Where(r => r.Name == name).Select(r => r.Id).ToListAsync(ct);
                foreach (var roleId in roleIds)
                    if (!await db.Set<UserRolesRow>().AnyAsync(ur => ur.UserId == id && ur.RoleId == roleId, ct))
                        await db.InsertAsync(new UserRolesRow { UserId = id, RoleId = roleId }, ct);
            }
            else
            {
                await db.Set<UserRolesRow>().Where(ur => ur.UserId == id &&
                        db.Set<RolesRow>().Any(r => r.Id == ur.RoleId && r.Name == name))
                    .ExecuteDeleteAsync(ct);
            }
            if (role == "Driver" || (assigned && role is "CustomerService" or "Maintenance") || (!assigned && role == "Staff"))
            {
                var isDriver = assigned && role == "Driver";
                await db.Set<StaffsRow>().Where(s => s.UserId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDriver, isDriver), ct);
            }
            // Vô hiệu hóa cả khi gọi lại lũy đẳng, để lệnh Redis bị lỗi có thể thử lại an toàn.
            await BumpAuthorizationAsync(db, id, ct);
        }, ct);

    public Task<AdministrativeUser> InvalidateSessionsAsync(int id, int actorUserId, CancellationToken ct) =>
        MutateAsync(id, actorUserId, "sessions:revoke", null, db => BumpAuthorizationAsync(db, id, ct), ct);

    private static Task<int> BumpAuthorizationAsync(VehicleRentalDbContext db, int id, CancellationToken ct) =>
        db.Set<UsersRow>().Where(u => u.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.AuthorizationVersion, u => u.AuthorizationVersion + 1), ct);

    private async Task<AdministrativeUser> MutateAsync(int id, int actorUserId, string action, string? reason,
        Func<VehicleRentalDbContext, Task> mutate, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var db = VehicleRentalDbContext.Create(connection, transaction);
        // Lấy khóa điều phối trước khi khóa tài khoản: việc kích hoạt và đổi vai trò tài xế không
        // được tranh chấp với kiểm tra tài xế còn trống trong transaction phân công.
        await db.AdvisoryLockAsync(724232001L, ct);
        // Mọi thao tác đổi vai trò/trạng thái của quản trị viên đều lấy cùng khóa này trước khi khóa dòng.
        await db.AdvisoryLockAsync(379123220L, ct);
        if (!await ActiveAdmins(db).AnyAsync(u => u.Id == actorUserId, ct))
            throw new UserAdministrationException(403, "An active administrator is required.");
        if (await db.LockByKeyAsync<UsersRow>(id, ct) is null) throw new UserAdministrationException(404, "User was not found.");
        await mutate(db);
        await db.InsertAsync(new UserAdministrationAuditRow
        {
            UserId = id, ActorUserId = actorUserId, Action = action, Reason = reason, IpAddress = auditContext?.IpAddress,
        }, ct);
        var user = await Project(db, db.Set<UsersRow>().Where(u => u.Id == id)).SingleAsync(ct);
        await transaction.CommitAsync(ct);
        return user;
    }

    private static IQueryable<UsersRow> ActiveAdmins(VehicleRentalDbContext db) =>
        db.Set<UsersRow>().Where(u => u.IsActive && (from ur in db.Set<UserRolesRow>()
            join r in db.Set<RolesRow>() on ur.RoleId equals r.Id
            where ur.UserId == u.Id && r.Name == "Admin" select ur.Id).Any());

    private static Task<bool> HasAnyRoleAsync(VehicleRentalDbContext db, int userId, string[] names, CancellationToken ct) =>
        (from ur in db.Set<UserRolesRow>() join r in db.Set<RolesRow>() on ur.RoleId equals r.Id
         where ur.UserId == userId && names.Contains(r.Name) select ur.Id).AnyAsync(ct);

    private static async Task ProtectLastAdminAsync(VehicleRentalDbContext db, int id, CancellationToken ct)
    {
        if (await ActiveAdmins(db).AnyAsync(u => u.Id == id, ct) && !await ActiveAdmins(db).AnyAsync(u => u.Id != id, ct))
            throw new UserAdministrationException(409, "The last active administrator cannot be removed or deactivated.");
    }

    private static async Task ValidateRoleProfileAsync(VehicleRentalDbContext db, int id, string role, CancellationToken ct)
    {
        if (!await db.Set<RolesRow>().AnyAsync(r => r.Name == role, ct))
            throw new UserAdministrationException(400, "Role was not found.");
        if (role == "Customer" && !await db.Set<CustomersRow>().AnyAsync(c => c.UserId == id, ct))
            throw new UserAdministrationException(409, "A customer profile is required for the Customer role.");
        if (role is "Staff" or "Manager" or "CustomerService" or "Driver" or "Maintenance"
            && (await db.LockWhereAsync<StaffsRow>(nameof(StaffsRow.UserId), [id], ct)).Count == 0)
            throw new UserAdministrationException(409, "An employee profile is required for this role.");
        if (role is "CustomerService" or "Driver" or "Maintenance")
        {
            if (!await HasAnyRoleAsync(db, id, ["Staff"], ct))
                throw new UserAdministrationException(409, "Assign the Staff role before assigning a staff specialty.");
            if (await HasAnyRoleAsync(db, id, StaffSpecialties.Where(name => name != role).ToArray(), ct))
                throw new UserAdministrationException(409, "Remove the existing staff specialty before assigning a different specialty.");
        }
        if (role == "Driver")
        {
            var today = await db.DatabaseTodayAsync(ct);
            if (!await db.Set<StaffsRow>().AnyAsync(s => s.UserId == id && s.LicenseNumber != null && s.LicenseNumber.Trim() != ""
                    && s.LicenseExpiryDate >= today && (s.LicenseIssueDate == null || s.LicenseIssueDate <= today), ct))
                throw new UserAdministrationException(409, "The Driver role requires a driver profile with a valid license.");
        }
    }
}
