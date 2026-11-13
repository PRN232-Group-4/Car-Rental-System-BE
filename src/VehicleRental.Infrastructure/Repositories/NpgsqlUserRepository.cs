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
        await using var db = VehicleRentalDbContext.Create(connection, transaction);
        var row = new UsersRow { Email = user.Email, Fullname = user.FullName, PasswordHash = user.PasswordHash };
        await db.InsertAsync(row, ct);
        await db.InsertAsync(new CustomersRow { UserId = row.Id }, ct);
        await EnsureCustomerRoleAsync(db, row.Id, ct);
        await transaction.CommitAsync(ct);
    }

    public async Task EnsureGoogleCustomerAsync(string email, string fullName, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var db = VehicleRentalDbContext.Create(connection, transaction);
        var existing = await db.Set<UsersRow>().Where(user => user.Email == email)
            .Select(user => (int?)user.Id).FirstOrDefaultAsync(ct);
        var userId = existing ?? await CreateGoogleUserAsync(db, email, fullName, ct);
        if (!await db.Set<CustomersRow>().AnyAsync(c => c.UserId == userId, ct))
            await db.InsertAsync(new CustomersRow { UserId = userId }, ct);
        await EnsureCustomerRoleAsync(db, userId, ct);
        await transaction.CommitAsync(ct);
    }

    public async Task UpdateAvatarAsync(string email, string avatarUrl, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        if (await db.Set<UsersRow>().Where(u => u.Email == email)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.Avatar, avatarUrl), ct) != 1)
            throw new InvalidOperationException("Authenticated user was not found.");
    }

    private static async Task<int> CreateGoogleUserAsync(VehicleRentalDbContext db, string email, string fullName, CancellationToken ct)
    {
        var row = new UsersRow { Email = email, Fullname = fullName, PasswordHash = null };
        await db.InsertAsync(row, ct);
        return row.Id;
    }

    private static async Task EnsureCustomerRoleAsync(VehicleRentalDbContext db, int userId, CancellationToken ct)
    {
        var roleId = await db.Set<RolesRow>().Where(r => r.Name == "Customer").Select(r => (int?)r.Id).FirstOrDefaultAsync(ct);
        if (roleId is null)
        {
            var role = new RolesRow { Name = "Customer" };
            await db.InsertAsync(role, ct);
            roleId = role.Id;
        }
        if (!await db.Set<UserRolesRow>().AnyAsync(ur => ur.UserId == userId && ur.RoleId == roleId, ct))
            await db.InsertAsync(new UserRolesRow { UserId = userId, RoleId = roleId.Value }, ct);
    }
}
