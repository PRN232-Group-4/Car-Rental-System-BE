using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.DTOs.Accounts;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Repositories;

public sealed class NpgsqlRoleRepository(NpgsqlDataSource dataSource) : IRoleRepository
{
    private const string Select = """
        SELECT r.id,r.name,(SELECT COUNT(DISTINCT ur.user_id)::int FROM user_roles ur WHERE ur.role_id=r.id)
        FROM roles r
        """;
    private const string Filter = " WHERE ($1::text IS NULL OR r.name ILIKE '%'||$1||'%' OR r.id::text=$1)";

    public async Task<RolePage> ListAsync(RoleListQuery query, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        await using var count = new NpgsqlCommand("SELECT COUNT(*)::int FROM roles r" + Filter, connection, transaction);
        count.Parameters.AddWithValue((object?)query.Search ?? DBNull.Value);
        var total = (int)(await count.ExecuteScalarAsync(ct))!;
        await using var command = new NpgsqlCommand(Select + Filter + " ORDER BY r.id DESC LIMIT $2 OFFSET $3", connection, transaction);
        command.Parameters.AddWithValue((object?)query.Search ?? DBNull.Value);
        command.Parameters.AddWithValue(query.PageSize);
        command.Parameters.AddWithValue(((long)query.Page - 1) * query.PageSize);
        var items = new List<RoleDetails>();
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) items.Add(Read(reader));
        await transaction.CommitAsync(ct);
        return new(items, query.Page, query.PageSize, total);
    }

    public async Task<RoleDetails?> FindAsync(int id, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand(Select + " WHERE r.id=$1");
        command.Parameters.AddWithValue(id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }

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
        await using var command = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM roles WHERE lower(name)=lower($1) AND ($2::int IS NULL OR id<>$2))", connection, transaction);
        command.Parameters.AddWithValue(name);
        command.Parameters.AddWithValue((object?)exceptId ?? DBNull.Value);
        if ((bool)(await command.ExecuteScalarAsync(ct))!) throw new ConflictException("role_name_exists", "A role with this name already exists.");
    }

    private static async Task<RoleDetails> RequireAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, int id, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(Select + " WHERE r.id=$1", connection, transaction);
        command.Parameters.AddWithValue(id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : throw new NotFoundException("role_not_found", "Role was not found.");
    }

    private static RoleDetails Read(NpgsqlDataReader reader)
    {
        var name = reader.GetString(1);
        var system = new[] { "Admin", "Manager", "Staff", "CustomerService", "Driver", "Maintenance", "Customer" }
            .Contains(name, StringComparer.OrdinalIgnoreCase);
        return new(reader.GetInt32(0), name, system, reader.GetInt32(2));
    }
}
