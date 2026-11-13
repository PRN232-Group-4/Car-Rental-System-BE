using Car_Rental_System_BE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.DTOs.Organization;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Car_Rental_System_BE.Domain.Services;
using Car_Rental_System_BE.Application.Validators;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Repositories;

public sealed class NpgsqlStaffRepository(NpgsqlDataSource dataSource) : IStaffRepository
{
    private static IQueryable<Staff> Project(VehicleRentalDbContext db, IQueryable<StaffsRow> staff) =>
        from s in staff
        join u in db.Set<UsersRow>() on s.UserId equals u.Id
        join department in db.Set<DepartmentsRow>() on s.DepartmentId equals department.Id into departments
        from d in departments.DefaultIfEmpty()
        join facility in db.Set<FacilitiesRow>() on s.FacilityId equals facility.Id into facilities
        from f in facilities.DefaultIfEmpty()
        select new Staff(s.Id, u.Id, u.Email, u.Fullname ?? "", u.Phone, s.DepartmentId, d == null ? null : d.Name,
            s.FacilityId, f == null ? null : f.Name, s.HiredDate,
            (from ur in db.Set<UserRolesRow>() join r in db.Set<RolesRow>() on ur.RoleId equals r.Id
             where ur.UserId == u.Id && r.Name == "Driver" select r.Id).Any(),
            s.LicenseNumber, s.LicenseType, s.LicenseIssueDate, s.LicenseExpiryDate, s.LicenseImg, s.Rating, u.IsActive,
            (from ur in db.Set<UserRolesRow>() join r in db.Set<RolesRow>() on ur.RoleId equals r.Id
             where ur.UserId == u.Id && (r.Name == "CustomerService" || r.Name == "Driver" || r.Name == "Maintenance")
             orderby r.Name select r.Name).FirstOrDefault(),
            u.Avatar, u.Gender, u.Address,
            db.Set<VehicleAssignmentsRow>().Where(a => a.DriverStaffId == s.Id).Select(a => a.RentalId).Distinct().Count(),
            (from a in db.Set<VehicleAssignmentsRow>() join r in db.Set<RentalsRow>() on a.RentalId equals r.Id
             where a.DriverStaffId == s.Id && a.Status == "assigned" && r.Status == "renting"
             orderby a.AssignedAt descending, a.Id descending select (int?)a.RentalId).FirstOrDefault())
        {
            Roles = (from ur in db.Set<UserRolesRow>() join r in db.Set<RolesRow>() on ur.RoleId equals r.Id
                     where ur.UserId == u.Id select r.Name).Distinct().OrderBy(name => name).ToArray()
        };

    public async Task<StaffPage> SearchAsync(string? search, bool? isDriver, int page, int pageSize, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        var normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var pattern = "%" + normalizedSearch + "%";
        var staff = db.Set<StaffsRow>().Where(s => db.Set<UsersRow>().Any(u => u.Id == s.UserId && u.IsActive
            && (normalizedSearch == null || EF.Functions.ILike(u.Email, pattern) || EF.Functions.ILike(u.Fullname!, pattern)
                || EF.Functions.ILike(u.Phone!, pattern) || EF.Functions.ILike(s.LicenseNumber!, pattern)))
            && (isDriver == null || (from ur in db.Set<UserRolesRow>() join r in db.Set<RolesRow>() on ur.RoleId equals r.Id
                where ur.UserId == s.UserId && r.Name == "Driver" select r.Id).Any() == isDriver));
        var total = await staff.CountAsync(ct);
        var offset = ((long)page - 1) * pageSize;
        var items = offset >= total ? [] : await Project(db, staff.OrderByDescending(s => s.Id).Skip((int)offset).Take(pageSize)).ToListAsync(ct);
        return new(items, page, pageSize, total);
    }

    public async Task<Staff?> FindByIdAsync(int staffId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await Project(db, db.Set<StaffsRow>().Where(s => s.Id == staffId)).SingleOrDefaultAsync(ct);
    }

    public async Task<Staff> CreateFromUserAsync(CreateStaffFromUser command, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var db = VehicleRentalDbContext.Create(connection, transaction);
        await db.AdvisoryLockAsync(724232001L, ct);
        if (await db.LockByKeyAsync<UsersRow>(command.UserId, ct) is not { IsActive: true }) throw new StaffNotFoundException();
        if (await db.Set<StaffsRow>().AnyAsync(staff => staff.UserId == command.UserId, ct))
            throw new StaffConflictException("Tài khoản đã có hồ sơ nhân viên. Hãy mở hồ sơ để cập nhật.");
        await ValidateReferencesAsync(db, command.DepartmentId, command.FacilityId, ct);
        var isDriver = await (from membership in db.Set<UserRolesRow>()
            join role in db.Set<RolesRow>() on membership.RoleId equals role.Id
            where membership.UserId == command.UserId && role.Name == "Driver"
            select role.Id).AnyAsync(ct);
        StaffValidator.ValidateDriver(isDriver, command.LicenseNumber, command.LicenseExpiryDate, command.LicenseIssueDate);
        // Existing account identity and roles are authoritative; no password or specialty replacement.
        var row = new StaffsRow
        {
            UserId = command.UserId, DepartmentId = command.DepartmentId, FacilityId = command.FacilityId, HiredDate = command.HiredDate,
            IsDriver = isDriver,
            LicenseNumber = isDriver ? command.LicenseNumber : null,
            LicenseType = isDriver ? command.LicenseType : null,
            LicenseIssueDate = isDriver ? command.LicenseIssueDate : null,
            LicenseExpiryDate = isDriver ? command.LicenseExpiryDate : null,
            LicenseImg = isDriver ? command.LicenseImageUrl : null,
        };
        await db.InsertAsync(row, ct);
        await EnsureRoleAsync(db, command.UserId, "Staff", ct);
        await transaction.CommitAsync(ct);
        return (await FindByIdAsync(row.Id, ct))!;
    }

    public async Task<Staff> CreateAsync(CreateStaff command, string passwordHash, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var db = VehicleRentalDbContext.Create(connection, transaction);
        StaffSpecialty.Validate(command.StaffRole, command.IsDriver);
        await ValidateReferencesAsync(db, command.DepartmentId, command.FacilityId, ct);
        try
        {
            var user = new UsersRow
            {
                Email = command.Email, Fullname = command.FullName, Phone = command.Phone, PasswordHash = passwordHash,
                Avatar = command.AvatarUrl, Gender = command.Gender, Address = command.Address,
            };
            await db.InsertAsync(user, ct);
            var staff = ToRow(user.Id, new UpdateStaff(command.FullName, command.Phone, command.DepartmentId, command.FacilityId, command.HiredDate,
                command.IsDriver, command.LicenseNumber, command.LicenseType, command.LicenseIssueDate, command.LicenseExpiryDate, command.LicenseImageUrl));
            await db.InsertAsync(staff, ct);
            await EnsureRoleAsync(db, user.Id, "Staff", ct);
            await SetSpecialtyAsync(db, user.Id, command.IsDriver ? "Driver" : command.StaffRole, ct);
            await transaction.CommitAsync(ct);
            return (await FindByIdAsync(staff.Id, ct))!;
        }
        catch (Exception error) when (error.SqlState() == PostgresErrorCodes.UniqueViolation)
        {
            throw new StaffConflictException("Email đã được sử dụng hoặc người dùng đã là staff.");
        }
    }

    public async Task<Staff> UpdateAsync(int staffId, UpdateStaff command, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var db = VehicleRentalDbContext.Create(connection, transaction);
        StaffSpecialty.Validate(command.StaffRole, command.IsDriver);
        await ValidateReferencesAsync(db, command.DepartmentId, command.FacilityId, ct);
        var userId = await db.Set<StaffsRow>().Where(s => s.Id == staffId).Select(s => (int?)s.UserId).FirstOrDefaultAsync(ct)
            ?? throw new StaffNotFoundException();
        await db.Set<StaffsRow>().Where(s => s.Id == staffId).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.DepartmentId, command.DepartmentId)
            .SetProperty(x => x.FacilityId, command.FacilityId)
            .SetProperty(x => x.HiredDate, command.HiredDate)
            .SetProperty(x => x.IsDriver, command.IsDriver)
            .SetProperty(x => x.LicenseNumber, command.LicenseNumber)
            .SetProperty(x => x.LicenseType, command.LicenseType)
            .SetProperty(x => x.LicenseIssueDate, command.LicenseIssueDate)
            .SetProperty(x => x.LicenseExpiryDate, command.LicenseExpiryDate)
            .SetProperty(x => x.LicenseImg, command.LicenseImageUrl), ct);
        await db.Set<UsersRow>().Where(u => u.Id == userId).ExecuteUpdateAsync(s => s
            .SetProperty(u => u.Fullname, command.FullName)
            .SetProperty(u => u.Phone, command.Phone)
            .SetProperty(u => u.Avatar, u => command.AvatarUrl ?? u.Avatar)
            .SetProperty(u => u.Gender, u => command.Gender ?? u.Gender)
            .SetProperty(u => u.Address, u => command.Address ?? u.Address), ct);
        await EnsureRoleAsync(db, userId, "Staff", ct);
        await SetSpecialtyAsync(db, userId, command.IsDriver ? "Driver" : command.StaffRole, ct);

        await transaction.CommitAsync(ct);
        return (await FindByIdAsync(staffId, ct))!;
    }

    public async Task DeactivateAsync(int staffId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        if (await db.Set<UsersRow>().Where(u => u.IsActive && db.Set<StaffsRow>().Any(s => s.Id == staffId && s.UserId == u.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false), ct) != 1)
            throw new StaffNotFoundException();
    }

    public Task<Staff> AssignDepartmentAsync(int userId, int departmentId, bool actorIsAdmin, CancellationToken ct) =>
        AssignOrganizationAsync(userId, departmentId, actorIsAdmin, false, ct);

    public Task<Staff> AssignFacilityAsync(int userId, int facilityId, bool actorIsAdmin, CancellationToken ct) =>
        AssignOrganizationAsync(userId, facilityId, actorIsAdmin, true, ct);

    private async Task<Staff> AssignOrganizationAsync(int userId, int unitId, bool actorIsAdmin, bool isFacility, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var db = VehicleRentalDbContext.Create(connection, transaction);
        // Serialize profile creation and membership changes for this account.
        if (await db.LockByKeyAsync<UsersRow>(userId, ct) is not { IsActive: true }) throw new StaffNotFoundException();

        var names = new HashSet<string>(await (from membership in db.Set<UserRolesRow>()
            join role in db.Set<RolesRow>() on membership.RoleId equals role.Id
            where membership.UserId == userId select role.Name).ToListAsync(ct), StringComparer.Ordinal);
        if (!actorIsAdmin && (names.Contains("Manager") || names.Contains("Admin")))
            throw new OrganizationAssignmentForbiddenException();
        if (!names.Contains("Manager") && !names.Contains("Staff"))
            throw new OrganizationReferenceNotFoundException("The account must already have the Staff or Manager role.");

        var unitExists = isFacility
            ? await db.LockByKeyAsync<FacilitiesRow>(unitId, ct, RowLock.Share) is not null
            : await db.LockByKeyAsync<DepartmentsRow>(unitId, ct, RowLock.Share) is not null;
        if (!unitExists)
            throw new OrganizationReferenceNotFoundException(isFacility ? "Facility was not found." : "Department was not found.");

        // The users row lock serializes this account, so update-or-insert replaces ON CONFLICT (user_id).
        var profile = db.Set<StaffsRow>().Where(s => s.UserId == userId);
        var updated = isFacility
            ? await profile.ExecuteUpdateAsync(s => s.SetProperty(x => x.FacilityId, unitId), ct)
            : await profile.ExecuteUpdateAsync(s => s.SetProperty(x => x.DepartmentId, unitId), ct);
        if (updated == 0)
            await db.InsertAsync(new StaffsRow
            {
                UserId = userId, FacilityId = isFacility ? unitId : null, DepartmentId = isFacility ? null : unitId,
            }, ct);
        var staffId = await profile.Select(s => s.Id).SingleAsync(ct);
        await transaction.CommitAsync(ct);
        return (await FindByIdAsync(staffId, ct))!;
    }

    private static async Task ValidateReferencesAsync(VehicleRentalDbContext db, int? departmentId, int? facilityId, CancellationToken ct)
    {
        if (departmentId.HasValue && !await db.Set<DepartmentsRow>().AnyAsync(unit => unit.Id == departmentId, ct))
            throw new OrganizationReferenceNotFoundException("Phòng ban không tồn tại.");
        if (facilityId.HasValue && !await db.Set<FacilitiesRow>().AnyAsync(unit => unit.Id == facilityId, ct))
            throw new OrganizationReferenceNotFoundException("Cơ sở không tồn tại.");
    }

    private static StaffsRow ToRow(int userId, UpdateStaff staff) => new()
    {
        UserId = userId, DepartmentId = staff.DepartmentId, FacilityId = staff.FacilityId, HiredDate = staff.HiredDate,
        IsDriver = staff.IsDriver, LicenseNumber = staff.LicenseNumber, LicenseType = staff.LicenseType,
        LicenseIssueDate = staff.LicenseIssueDate, LicenseExpiryDate = staff.LicenseExpiryDate, LicenseImg = staff.LicenseImageUrl,
    };

    private static readonly string[] Specialties = ["CustomerService", "Driver", "Maintenance"];

    private static async Task SetSpecialtyAsync(VehicleRentalDbContext db, int userId, string? roleName, CancellationToken ct)
    {
        await db.Set<UserRolesRow>().Where(ur => ur.UserId == userId &&
                db.Set<RolesRow>().Any(r => r.Id == ur.RoleId && Specialties.Contains(r.Name)))
            .ExecuteDeleteAsync(ct);
        if (roleName is not null) await EnsureRoleAsync(db, userId, roleName, ct);
    }

    private static async Task EnsureRoleAsync(VehicleRentalDbContext db, int userId, string roleName, CancellationToken ct)
    {
        if (!await db.Set<RolesRow>().AnyAsync(r => r.Name == roleName, ct))
            await db.InsertAsync(new RolesRow { Name = roleName }, ct);
        var roleIds = await db.Set<RolesRow>().Where(r => r.Name == roleName).Select(r => r.Id).ToListAsync(ct);
        foreach (var roleId in roleIds)
            if (!await db.Set<UserRolesRow>().AnyAsync(ur => ur.UserId == userId && ur.RoleId == roleId, ct))
                await db.InsertAsync(new UserRolesRow { UserId = userId, RoleId = roleId }, ct);
    }
}
