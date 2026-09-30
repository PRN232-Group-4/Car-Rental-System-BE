using Car_Rental_System_BE.Application.Registration;
using Car_Rental_System_BE.Domain.Users;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure;

public sealed class NpgsqlUserRepository(NpgsqlDataSource dataSource) : IUserRepository
{
    public async Task<bool> EmailExistsAsync(string email, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("SELECT EXISTS (SELECT 1 FROM users WHERE email = $1)");
        command.Parameters.AddWithValue(email);
        return (bool)(await command.ExecuteScalarAsync(ct))!;
    }

    public async Task CreateCustomerAsync(User user, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var command = new NpgsqlCommand("INSERT INTO users (email, fullname, password_hash) VALUES ($1, $2, $3) RETURNING id", connection, transaction);
        command.Parameters.AddWithValue(user.Email); command.Parameters.AddWithValue(user.FullName); command.Parameters.AddWithValue(user.PasswordHash);
        var userId = (int)(await command.ExecuteScalarAsync(ct))!;
        await using var customer = new NpgsqlCommand("INSERT INTO customers (user_id) VALUES ($1)", connection, transaction);
        customer.Parameters.AddWithValue(userId); await customer.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task EnsureGoogleCustomerAsync(string email, string fullName, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var find = new NpgsqlCommand("SELECT id FROM users WHERE email = $1", connection, transaction);
        find.Parameters.AddWithValue(email);
        var existing = await find.ExecuteScalarAsync(ct);
        var userId = existing is int id ? id : await CreateGoogleUserAsync(connection, transaction, email, fullName, ct);
        await using var customer = new NpgsqlCommand("INSERT INTO customers (user_id) SELECT $1 WHERE NOT EXISTS (SELECT 1 FROM customers WHERE user_id = $1)", connection, transaction);
        customer.Parameters.AddWithValue(userId);
        await customer.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task UpdateAvatarAsync(string email, string avatarUrl, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("UPDATE users SET avatar = $1 WHERE email = $2");
        command.Parameters.AddWithValue(avatarUrl); command.Parameters.AddWithValue(email);
        if (await command.ExecuteNonQueryAsync(ct) != 1) throw new InvalidOperationException("Authenticated user was not found.");
    }

    private static async Task<int> CreateGoogleUserAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string email, string fullName, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("INSERT INTO users (email, fullname, password_hash) VALUES ($1, $2, NULL) RETURNING id", connection, transaction);
        command.Parameters.AddWithValue(email); command.Parameters.AddWithValue(fullName);
        return (int)(await command.ExecuteScalarAsync(ct))!;
    }
}
