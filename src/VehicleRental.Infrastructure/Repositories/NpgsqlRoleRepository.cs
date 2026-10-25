using Microsoft.EntityFrameworkCore;
using Car_Rental_System_BE.Infrastructure.Persistence;
using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.DTOs.Accounts;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Repositories;

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

    public Task<RoleDetails> CreateAsync(string name, CancellationToken ct) => MutateAsync(async (connection, transaction) =>
    {
        await EnsureUniqueAsync(connection, transaction, name, null, ct);
        await using var command = new NpgsqlCommand("INSERT INTO roles(name) VALUES($1) RETURNING id", connection, transaction);
        command.Parameters.AddWithValue(name);
        return new((int)(await command.ExecuteScalarAsync(ct))!, name, false, 0);
    }, ct);

    public Task<RoleDetails> UpdateAsync(int id, string name, CancellationToken ct) => MutateAsync(async (connection, transaction) =>
    {
        var role = await RequireAsync(connection, transaction, id, ct);
        if (role.Name == name) return role;
        ProtectRole(role);
        await EnsureUniqueAsync(connection, transaction, name, id, ct);
        await using var command = new NpgsqlCommand("UPDATE roles SET name=$2 WHERE id=$1", connection, transaction);
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(name);
        await command.ExecuteNonQueryAsync(ct);
        return role with { Name = name };
    }, ct);

    public async Task DeleteAsync(int id, CancellationToken ct) => await MutateAsync(async (connection, transaction) =>
    {
        var role = await RequireAsync(connection, transaction, id, ct);
        ProtectRole(role);
        await using var command = new NpgsqlCommand("DELETE FROM roles WHERE id=$1", connection, transaction);
        command.Parameters.AddWithValue(id);
        await command.ExecuteNonQueryAsync(ct);
        return role;
    }, ct);

    private async Task<RoleDetails> MutateAsync(Func<NpgsqlConnection, NpgsqlTransaction, Task<RoleDetails>> mutate, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        // Match account administration lock ordering. Table locks additionally serialize
        // legacy staff/registration writers that do not acquire the admin advisory lock.
        // No users rows are updated/locked here: assigned role renames are rejected.
        await using var locks = new NpgsqlCommand("""
            SELECT pg_advisory_xact_lock(724232001::bigint);
            SELECT pg_advisory_xact_lock(379123220);
            LOCK TABLE roles, user_roles IN SHARE ROW EXCLUSIVE MODE;
            """, connection, transaction);
        await locks.ExecuteNonQueryAsync(ct);
        var role = await mutate(connection, transaction);
        await transaction.CommitAsync(ct);
        return role;
    }

    private static void ProtectRole(RoleDetails role)
    {
        if (role.IsSystem) throw new ConflictException("system_role_protected", "System roles cannot be renamed or deleted.");
        if (role.UserCount > 0) throw new ConflictException("role_in_use", "Remove all user assignments before renaming or deleting this role.");
    }

    private static async Task EnsureUniqueAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string name, int? exceptId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(connection, transaction);
        if (await db.Set<RolesRow>().AnyAsync(role => role.Name.ToLower() == name.ToLower() &&
                (exceptId == null || role.Id != exceptId), ct))
            throw new ConflictException("role_name_exists", "A role with this name already exists.");
    }

    private static async Task<RoleDetails> RequireAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, int id, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(connection, transaction);
        return await FindAsync(db, id, ct) ?? throw new NotFoundException("role_not_found", "Role was not found.");
    }

}
