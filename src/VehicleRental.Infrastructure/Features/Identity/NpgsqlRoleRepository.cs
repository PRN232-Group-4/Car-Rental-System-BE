using Microsoft.EntityFrameworkCore;
using Car_Rental_System_BE.Infrastructure.Persistence;
using Car_Rental_System_BE.Application.Common.Errors;
using Npgsql;
using Car_Rental_System_BE.Application.Features.Identity;

namespace Car_Rental_System_BE.Infrastructure.Features.Identity;

public sealed class NpgsqlRoleRepository(NpgsqlDataSource dataSource) : IRoleRepository
{
    public async Task<RolePage> ListAsync(RoleListQuery query, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        await using var db = VehicleRentalDbContext.Create(connection, transaction);
        var filtered = db.Set<RolesRow>().Where(role => query.Search == null ||
            EF.Functions.ILike(role.Name, "%" + query.Search + "%") || role.Id.ToString() == query.Search);
        var total = await filtered.CountAsync(ct);
        var offset = ((long)query.Page - 1) * query.PageSize;
        var rows = offset >= total ? [] : await filtered.OrderByDescending(role => role.Id)
            .Skip((int)offset).Take(query.PageSize)
            .Select(role => new { role.Id, role.Name,
                Count = db.Set<UserRolesRow>().Where(member => member.RoleId == role.Id)
                    .Select(member => member.UserId).Distinct().Count() }).ToListAsync(ct);
        var items = rows.Select(row => Details(row.Id, row.Name, row.Count)).ToList();
        await transaction.CommitAsync(ct);
        return new(items, query.Page, query.PageSize, total);
    }

    public async Task<RoleDetails?> FindAsync(int id, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await FindAsync(db, id, ct);
    }

    private static async Task<RoleDetails?> FindAsync(VehicleRentalDbContext db, int id, CancellationToken ct)
    {
        var row = await db.Set<RolesRow>().Where(role => role.Id == id)
            .Select(role => new { role.Id, role.Name,
                Count = db.Set<UserRolesRow>().Where(member => member.RoleId == role.Id)
                    .Select(member => member.UserId).Distinct().Count() }).FirstOrDefaultAsync(ct);
        return row is null ? null : Details(row.Id, row.Name, row.Count);
    }

    private static RoleDetails Details(int id, string name, int count) => new(id, name,
        new[] { "Admin", "Manager", "Staff", "CustomerService", "Driver", "Maintenance", "Customer" }
            .Contains(name, StringComparer.OrdinalIgnoreCase), count);

    public Task<RoleDetails> CreateAsync(string name, CancellationToken ct) => MutateAsync(async db =>
    {
        await EnsureUniqueAsync(db, name, null, ct);
        var row = new RolesRow { Name = name };
        await db.InsertAsync(row, ct);
        return new(row.Id, name, false, 0);
    }, ct);

    public Task<RoleDetails> UpdateAsync(int id, string name, CancellationToken ct) => MutateAsync(async db =>
    {
        var role = await RequireAsync(db, id, ct);
        if (role.Name == name) return role;
        ProtectRole(role);
        await EnsureUniqueAsync(db, name, id, ct);
        await db.Set<RolesRow>().Where(r => r.Id == id).ExecuteUpdateAsync(s => s.SetProperty(r => r.Name, name), ct);
        return role with { Name = name };
    }, ct);

    public async Task DeleteAsync(int id, CancellationToken ct) => await MutateAsync(async db =>
    {
        var role = await RequireAsync(db, id, ct);
        ProtectRole(role);
        await db.Set<RolesRow>().Where(r => r.Id == id).ExecuteDeleteAsync(ct);
        return role;
    }, ct);

    private async Task<RoleDetails> MutateAsync(Func<VehicleRentalDbContext, Task<RoleDetails>> mutate, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var db = VehicleRentalDbContext.Create(connection, transaction);
        // Khớp thứ tự khóa của quản trị tài khoản. Khóa bảng còn tuần tự hóa các luồng ghi nhân
        // viên/đăng ký cũ không lấy advisory lock quản trị. Không cập nhật/khóa dòng users nào ở
        // đây: đổi tên vai trò đang được gán sẽ bị từ chối.
        await db.AdvisoryLockAsync(724232001L, ct);
        await db.AdvisoryLockAsync(379123220L, ct);
        await db.LockTablesAsync("SHARE ROW EXCLUSIVE", ct, typeof(RolesRow), typeof(UserRolesRow));
        var role = await mutate(db);
        await transaction.CommitAsync(ct);
        return role;
    }

    private static void ProtectRole(RoleDetails role)
    {
        if (role.IsSystem) throw new ConflictException("system_role_protected", "System roles cannot be renamed or deleted.");
        if (role.UserCount > 0) throw new ConflictException("role_in_use", "Remove all user assignments before renaming or deleting this role.");
    }

    private static async Task EnsureUniqueAsync(VehicleRentalDbContext db, string name, int? exceptId, CancellationToken ct)
    {
        if (await db.Set<RolesRow>().AnyAsync(role => role.Name.ToLower() == name.ToLower() &&
                (exceptId == null || role.Id != exceptId), ct))
            throw new ConflictException("role_name_exists", "A role with this name already exists.");
    }

    private static async Task<RoleDetails> RequireAsync(VehicleRentalDbContext db, int id, CancellationToken ct) =>
        await FindAsync(db, id, ct) ?? throw new NotFoundException("role_not_found", "Role was not found.");
}
