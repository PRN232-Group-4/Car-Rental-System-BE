using Microsoft.EntityFrameworkCore;
using Car_Rental_System_BE.Infrastructure.Persistence;
using Car_Rental_System_BE.Application.Services.Security;
using Car_Rental_System_BE.Domain.Entities;
using Car_Rental_System_BE.Domain.Interfaces;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Repositories;

public sealed class NpgsqlUserRepository(NpgsqlDataSource dataSource) : IUserRepository
{
    public async Task<bool> EmailExistsAsync(string email, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await db.Set<UsersRow>().AnyAsync(user => user.Email == email, ct);
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
        await EnsureCustomerRoleAsync(connection, transaction, userId, ct);
        await transaction.CommitAsync(ct);
    }

    public async Task EnsureGoogleCustomerAsync(string email, string fullName, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var db = VehicleRentalDbContext.Create(connection, transaction);
        var existing = await db.Set<UsersRow>().Where(user => user.Email == email)
            .Select(user => (int?)user.Id).FirstOrDefaultAsync(ct);
        var userId = existing ?? await CreateGoogleUserAsync(connection, transaction, email, fullName, ct);
        await using var customer = new NpgsqlCommand("INSERT INTO customers (user_id) SELECT $1 WHERE NOT EXISTS (SELECT 1 FROM customers WHERE user_id = $1)", connection, transaction);
        customer.Parameters.AddWithValue(userId);
        await customer.ExecuteNonQueryAsync(ct);
        await EnsureCustomerRoleAsync(connection, transaction, userId, ct);
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

    private static async Task EnsureCustomerRoleAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, int userId, CancellationToken ct)
    {
        await using var role = new NpgsqlCommand("INSERT INTO roles (name) SELECT 'Customer' WHERE NOT EXISTS (SELECT 1 FROM roles WHERE name = 'Customer')", connection, transaction);
        await role.ExecuteNonQueryAsync(ct);
        await using var membership = new NpgsqlCommand("INSERT INTO user_roles (user_id, role_id) SELECT $1, id FROM roles WHERE name = 'Customer' AND NOT EXISTS (SELECT 1 FROM user_roles ur WHERE ur.user_id = $1 AND ur.role_id = roles.id)", connection, transaction);
        membership.Parameters.AddWithValue(userId);
        await membership.ExecuteNonQueryAsync(ct);
    }
}
