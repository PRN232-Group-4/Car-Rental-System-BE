using Car_Rental_System_BE.Application.DTOs.Accounts;
using Car_Rental_System_BE.Application.Interfaces.External;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Repositories;

public sealed class NpgsqlUserAdministrationRepository(NpgsqlDataSource dataSource, IAuditRequestContext? auditContext = null) : IUserAdministrationRepository
{
    private const string Select = """
        SELECT u.id,u.email,COALESCE(u.fullname,''),u.phone,u.avatar,u.gender,u.address,u.is_active,u.created_at,
          ARRAY(SELECT DISTINCT r.name FROM user_roles ur JOIN roles r ON r.id=ur.role_id WHERE ur.user_id=u.id ORDER BY r.name),
          (SELECT c.id FROM customers c WHERE c.user_id=u.id), (SELECT s.id FROM staffs s WHERE s.user_id=u.id)
        FROM users u
        """;
    private const string Filter = """
         WHERE ($1::text IS NULL OR u.email ILIKE '%'||$1||'%' OR u.fullname ILIKE '%'||$1||'%' OR u.phone ILIKE '%'||$1||'%')
           AND ($2::text IS NULL OR EXISTS(SELECT 1 FROM user_roles ur JOIN roles r ON r.id=ur.role_id WHERE ur.user_id=u.id AND lower(r.name)=lower($2)))
           AND ($3::boolean IS NULL OR u.is_active=$3)
        """;

    public async Task<AdministrativeUserPage> ListAsync(UserListQuery query, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        await using var count = new NpgsqlCommand("SELECT COUNT(*) FROM users u" + Filter, connection, transaction);
        AddFilters(count, query);
        var total = Convert.ToInt32(await count.ExecuteScalarAsync(ct));
        await using var command = new NpgsqlCommand(Select + Filter + " ORDER BY u.id DESC LIMIT $4 OFFSET $5", connection, transaction);
        AddFilters(command, query);
        command.Parameters.AddWithValue(query.PageSize);
        command.Parameters.AddWithValue(((long)query.Page - 1) * query.PageSize);
        var items = new List<AdministrativeUser>();
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) items.Add(Read(reader));
        await transaction.CommitAsync(ct);
        return new(items, query.Page, query.PageSize, total);
    }

    public async Task<AdministrativeUser?> FindAsync(int id, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand(Select + " WHERE u.id=$1");
        command.Parameters.AddWithValue(id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }

    public Task<AdministrativeUser> SetStatusAsync(int id, bool isActive, string reason, int actorUserId, CancellationToken ct) =>
        MutateAsync(id, actorUserId, isActive ? "activate" : "deactivate", reason, async (connection, transaction) =>
        {
            if (!isActive)
            {
                if (id == actorUserId) throw new UserAdministrationException(409, "You cannot deactivate your own account.");
                await ProtectLastAdminAsync(connection, transaction, id, ct);
            }
            await ExecuteAsync(connection, transaction, "UPDATE users SET is_active=$2, authorization_version=authorization_version+1 WHERE id=$1", ct, id, isActive);
        }, ct);

    public Task<AdministrativeUser> SetRoleAsync(int id, string role, bool assigned, int actorUserId, CancellationToken ct) =>
        MutateAsync(id, actorUserId, assigned ? "role:add" : "role:remove", role, async (connection, transaction) =>
        {
            // Resolve to the stored spelling before applying built-in invariants: e.g.
            // a request for 'admin' must still run the last active Admin protection.
            await using (var canonical = new NpgsqlCommand("SELECT name FROM roles WHERE lower(name)=lower($1)", connection, transaction))
            {
                canonical.Parameters.AddWithValue(role);
                role = (string?)await canonical.ExecuteScalarAsync(ct)
                    ?? throw new UserAdministrationException(400, "Role was not found.");
            }
            if (assigned) await ValidateRoleProfileAsync(connection, transaction, id, role, ct);
            if (!assigned && role == "Admin") await ProtectLastAdminAsync(connection, transaction, id, ct);
            if (!assigned && role == "Staff" && await ExistsAsync(connection, transaction,
                "SELECT EXISTS(SELECT 1 FROM user_roles ur JOIN roles r ON r.id=ur.role_id WHERE ur.user_id=$1 AND r.name IN ('CustomerService','Driver','Maintenance'))", ct, id))
                throw new UserAdministrationException(409, "Remove the staff specialty roles before removing Staff.");
            if (assigned)
            {
                await ExecuteAsync(connection, transaction, """
                    INSERT INTO user_roles(user_id,role_id) SELECT $1,id FROM roles WHERE name=$2
                    AND NOT EXISTS(SELECT 1 FROM user_roles WHERE user_id=$1 AND role_id=roles.id)
                    """, ct, id, role);
            }
            else await ExecuteAsync(connection, transaction,
                "DELETE FROM user_roles WHERE user_id=$1 AND role_id IN(SELECT id FROM roles WHERE name=$2)", ct, id, role);
            if (role == "Driver" || (assigned && role is "CustomerService" or "Maintenance") || (!assigned && role == "Staff"))
                await ExecuteAsync(connection, transaction, "UPDATE staffs SET is_driver=$2 WHERE user_id=$1", ct, id, assigned && role == "Driver");
            // Also invalidate on idempotent retries, so a failed Redis call can be retried safely.
            await ExecuteAsync(connection, transaction, "UPDATE users SET authorization_version=authorization_version+1 WHERE id=$1", ct, id);
        }, ct);

    public Task<AdministrativeUser> InvalidateSessionsAsync(int id, int actorUserId, CancellationToken ct) =>
        MutateAsync(id, actorUserId, "sessions:revoke", null, async (connection, transaction) =>
            await ExecuteAsync(connection, transaction, "UPDATE users SET authorization_version=authorization_version+1 WHERE id=$1", ct, id), ct);

    private async Task<AdministrativeUser> MutateAsync(int id, int actorUserId, string action, string? reason,
        Func<NpgsqlConnection, NpgsqlTransaction, Task> mutate, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        // Match the dispatcher lock before taking account locks: activation and driver-role
        // changes must not race driver availability checks in an assignment transaction.
        await ExecuteAsync(connection, transaction, "SELECT pg_advisory_xact_lock(724232001::bigint)", ct);
        // All administrator membership/status mutations acquire the same lock before row locks.
        await ExecuteAsync(connection, transaction, "SELECT pg_advisory_xact_lock(379123220)", ct);
        if (!await ExistsAsync(connection, transaction, """
            SELECT EXISTS(SELECT 1 FROM users u JOIN user_roles ur ON ur.user_id=u.id JOIN roles r ON r.id=ur.role_id
            WHERE u.id=$1 AND u.is_active AND r.name='Admin')
            """, ct, actorUserId)) throw new UserAdministrationException(403, "An active administrator is required.");
        await using var target = new NpgsqlCommand("SELECT id FROM users WHERE id=$1 FOR UPDATE", connection, transaction);
        target.Parameters.AddWithValue(id);
        if (await target.ExecuteScalarAsync(ct) is null) throw new UserAdministrationException(404, "User was not found.");
        await mutate(connection, transaction);
        await ExecuteAsync(connection, transaction,
            "INSERT INTO user_administration_audit(user_id,actor_user_id,action,reason,ip_address) VALUES($1,$2,$3,$4,$5)", ct,
            id, actorUserId, action, (object?)reason ?? DBNull.Value, (object?)auditContext?.IpAddress ?? DBNull.Value);
        await using var read = new NpgsqlCommand(Select + " WHERE u.id=$1", connection, transaction);
        read.Parameters.AddWithValue(id);
        AdministrativeUser user;
        await using (var reader = await read.ExecuteReaderAsync(ct)) { await reader.ReadAsync(ct); user = Read(reader); }
        await transaction.CommitAsync(ct);
        return user;
    }

    private static async Task ProtectLastAdminAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, int id, CancellationToken ct)
    {
        if (await ExistsAsync(connection, transaction, """
            SELECT EXISTS(SELECT 1 FROM users u JOIN user_roles ur ON ur.user_id=u.id JOIN roles r ON r.id=ur.role_id
              WHERE u.id=$1 AND u.is_active AND r.name='Admin')
              AND NOT EXISTS(SELECT 1 FROM users u JOIN user_roles ur ON ur.user_id=u.id JOIN roles r ON r.id=ur.role_id
              WHERE u.id<>$1 AND u.is_active AND r.name='Admin')
            """, ct, id)) throw new UserAdministrationException(409, "The last active administrator cannot be removed or deactivated.");
    }

    private static async Task ValidateRoleProfileAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, int id, string role, CancellationToken ct)
    {
        if (!await ExistsAsync(connection, transaction, "SELECT EXISTS(SELECT 1 FROM roles WHERE name=$1)", ct, role))
            throw new UserAdministrationException(400, "Role was not found.");
        if (role == "Customer" && !await ExistsAsync(connection, transaction, "SELECT EXISTS(SELECT 1 FROM customers WHERE user_id=$1)", ct, id))
            throw new UserAdministrationException(409, "A customer profile is required for the Customer role.");
        if (role is "Staff" or "Manager" or "CustomerService" or "Driver" or "Maintenance")
        {
            await using var profileLock = new NpgsqlCommand("SELECT id FROM staffs WHERE user_id=$1 FOR UPDATE", connection, transaction);
            profileLock.Parameters.AddWithValue(id);
            if (await profileLock.ExecuteScalarAsync(ct) is null) throw new UserAdministrationException(409, "An employee profile is required for this role.");
        }
        if (role is "CustomerService" or "Driver" or "Maintenance")
        {
            if (!await ExistsAsync(connection, transaction, "SELECT EXISTS(SELECT 1 FROM user_roles ur JOIN roles r ON r.id=ur.role_id WHERE ur.user_id=$1 AND r.name='Staff')", ct, id))
                throw new UserAdministrationException(409, "Assign the Staff role before assigning a staff specialty.");
            if (await ExistsAsync(connection, transaction, "SELECT EXISTS(SELECT 1 FROM user_roles ur JOIN roles r ON r.id=ur.role_id WHERE ur.user_id=$1 AND r.name IN ('CustomerService','Driver','Maintenance') AND r.name<>$2)", ct, id, role))
                throw new UserAdministrationException(409, "Remove the existing staff specialty before assigning a different specialty.");
        }
        if (role == "Driver" && !await ExistsAsync(connection, transaction,
            "SELECT EXISTS(SELECT 1 FROM staffs WHERE user_id=$1 AND NULLIF(btrim(license_number),'') IS NOT NULL AND license_expiry_date>=CURRENT_DATE AND (license_issue_date IS NULL OR license_issue_date<=CURRENT_DATE))", ct, id))
            throw new UserAdministrationException(409, "The Driver role requires a driver profile with a valid license.");
    }

    private static async Task<bool> ExistsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken ct, params object[] values)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var value in values) command.Parameters.AddWithValue(value);
        return (bool)(await command.ExecuteScalarAsync(ct))!;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken ct, params object[] values)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var value in values) command.Parameters.AddWithValue(value);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static void AddFilters(NpgsqlCommand command, UserListQuery query)
    {
        command.Parameters.AddWithValue((object?)query.Search ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)query.Role ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)query.IsActive ?? DBNull.Value);
    }

    private static AdministrativeUser Read(NpgsqlDataReader r) => new(r.GetInt32(0), r.GetString(1), r.GetString(2),
        Text(r, 3), Text(r, 4), Text(r, 5), Text(r, 6), r.GetBoolean(7), r.GetDateTime(8), r.GetFieldValue<string[]>(9),
        r.IsDBNull(10) ? null : r.GetInt32(10), r.IsDBNull(11) ? null : r.GetInt32(11));
    private static string? Text(NpgsqlDataReader r, int ordinal) => r.IsDBNull(ordinal) ? null : r.GetString(ordinal);
}
