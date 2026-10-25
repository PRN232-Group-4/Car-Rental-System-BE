using Microsoft.EntityFrameworkCore;
using Car_Rental_System_BE.Infrastructure.Persistence;
using Car_Rental_System_BE.Application.DTOs.Accounts;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Repositories;

public sealed class NpgsqlAccountRepository(NpgsqlDataSource dataSource) : IAccountRepository
{
    public async Task<Account?> FindByEmailAsync(string email, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        var row = await (from user in db.Set<UsersRow>()
            where user.Email == email && user.IsActive
            from staff in db.Set<StaffsRow>().Where(s => s.UserId == user.Id).DefaultIfEmpty()
            from department in db.Set<DepartmentsRow>().Where(d => d.Id == staff.DepartmentId).DefaultIfEmpty()
            from facility in db.Set<FacilitiesRow>().Where(f => f.Id == staff.FacilityId).DefaultIfEmpty()
            select new
            {
                user.Id, user.Email, user.Fullname, user.Gender, user.Phone, user.Avatar, user.PasswordHash,
                StaffId = (int?)staff.Id, DepartmentId = (int?)staff.DepartmentId,
                DepartmentName = department.Name, FacilityId = (int?)staff.FacilityId,
                FacilityName = facility.Name, user.AuthorizationVersion,
                Roles = (from membership in db.Set<UserRolesRow>()
                    join role in db.Set<RolesRow>() on membership.RoleId equals role.Id
                    where membership.UserId == user.Id select role.Name).ToArray()
            }).FirstOrDefaultAsync(ct);
        return row is null ? null : new Account(row.Id, row.Email, row.Fullname ?? "", row.Gender, row.Phone,
            row.Avatar, row.PasswordHash, row.Roles, row.StaffId, row.DepartmentId, row.DepartmentName,
            row.FacilityId, row.AuthorizationVersion, row.FacilityName);
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

