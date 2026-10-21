using Car_Rental_System_BE.Application.DTOs.Accounts;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Repositories;

public sealed class NpgsqlAccountRepository(NpgsqlDataSource dataSource) : IAccountRepository
{
    public async Task<Account?> FindByEmailAsync(string email, CancellationToken ct)
    {
        const string sql = """
            SELECT u.id, u.email, COALESCE(u.fullname, ''), u.gender, u.phone, u.avatar, u.password_hash,
                   COALESCE(array_agg(r.name) FILTER (WHERE r.name IS NOT NULL), ARRAY[]::varchar[]), s.id, s.department_id, d.name, s.facility_id, u.authorization_version, f.name
            FROM users u
            LEFT JOIN staffs s ON s.user_id = u.id
            LEFT JOIN departments d ON d.id = s.department_id
            LEFT JOIN facilities f ON f.id = s.facility_id
            LEFT JOIN user_roles ur ON ur.user_id = u.id
            LEFT JOIN roles r ON r.id = ur.role_id
            WHERE u.email = $1 AND u.is_active = TRUE
            GROUP BY u.id, u.email, u.fullname, u.gender, u.phone, u.avatar, u.password_hash, s.id, d.name, f.name
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(email);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new Account(reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetFieldValue<string[]>(7), reader.IsDBNull(8) ? null : reader.GetInt32(8), reader.IsDBNull(9) ? null : reader.GetInt32(9), reader.IsDBNull(10) ? null : reader.GetString(10), reader.IsDBNull(11) ? null : reader.GetInt32(11), reader.GetInt32(12), reader.IsDBNull(13) ? null : reader.GetString(13));
    }

    public async Task UpdateProfileAsync(string email, AccountProfile profile, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("UPDATE users SET fullname = $1, gender = $2, phone = $3 WHERE email = $4 AND is_active = TRUE");
        command.Parameters.AddWithValue(profile.FullName);
        command.Parameters.AddWithValue((object?)profile.Gender ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)profile.Phone ?? DBNull.Value);
        command.Parameters.AddWithValue(email);
        if (await command.ExecuteNonQueryAsync(ct) != 1) throw new AccountNotFoundException();
    }

    public async Task UpdatePasswordHashAsync(string email, string passwordHash, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("UPDATE users SET password_hash = $1 WHERE email = $2 AND is_active = TRUE");
        command.Parameters.AddWithValue(passwordHash);
        command.Parameters.AddWithValue(email);
        if (await command.ExecuteNonQueryAsync(ct) != 1) throw new AccountNotFoundException();
    }
}

