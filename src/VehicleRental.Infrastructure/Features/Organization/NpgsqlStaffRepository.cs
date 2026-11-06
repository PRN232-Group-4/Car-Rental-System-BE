using Car_Rental_System_BE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Features.Organization;
using Car_Rental_System_BE.Domain.Services;
using Car_Rental_System_BE.Application.Validators;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Features.Organization;

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
        await using (var dispatcher = new NpgsqlCommand("SELECT pg_advisory_xact_lock(724232001::bigint)", connection, transaction))
            await dispatcher.ExecuteNonQueryAsync(ct);
        await using (var account = new NpgsqlCommand("SELECT id FROM users WHERE id=$1 AND is_active FOR UPDATE", connection, transaction))
        {
            account.Parameters.AddWithValue(command.UserId);
            if (await account.ExecuteScalarAsync(ct) is null) throw new StaffNotFoundException();
        }
        await using var db = VehicleRentalDbContext.Create(connection, transaction);
        if (await db.Set<StaffsRow>().AnyAsync(staff => staff.UserId == command.UserId, ct))
            throw new StaffConflictException("Tài khoản đã có hồ sơ nhân viên. Hãy mở hồ sơ để cập nhật.");
        await ValidateReferencesAsync(connection, transaction, command.DepartmentId, command.FacilityId, ct);
        var isDriver = await (from membership in db.Set<UserRolesRow>()
            join role in db.Set<RolesRow>() on membership.RoleId equals role.Id
            where membership.UserId == command.UserId && role.Name == "Driver"
            select role.Id).AnyAsync(ct);
        StaffValidator.ValidateDriver(isDriver, command.LicenseNumber, command.LicenseExpiryDate, command.LicenseIssueDate);
        // Existing account identity and roles are authoritative; no password or specialty replacement.
        await using var insert = new NpgsqlCommand("""
            INSERT INTO staffs(user_id,department_id,facility_id,hired_date,is_driver,
                license_number,license_type,license_issue_date,license_expiry_date,license_img)
            VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10) RETURNING id
            """, connection, transaction);
        insert.Parameters.AddWithValue(command.UserId);
        insert.Parameters.AddWithValue((object?)command.DepartmentId ?? DBNull.Value);
        insert.Parameters.AddWithValue((object?)command.FacilityId ?? DBNull.Value);
        insert.Parameters.AddWithValue((object?)command.HiredDate ?? DBNull.Value);
        insert.Parameters.AddWithValue(isDriver);
        insert.Parameters.AddWithValue(isDriver ? (object?)command.LicenseNumber ?? DBNull.Value : DBNull.Value);
        insert.Parameters.AddWithValue(isDriver ? (object?)command.LicenseType ?? DBNull.Value : DBNull.Value);
        insert.Parameters.AddWithValue(isDriver ? (object?)command.LicenseIssueDate ?? DBNull.Value : DBNull.Value);
        insert.Parameters.AddWithValue(isDriver ? (object?)command.LicenseExpiryDate ?? DBNull.Value : DBNull.Value);
        insert.Parameters.AddWithValue(isDriver ? (object?)command.LicenseImageUrl ?? DBNull.Value : DBNull.Value);
        var staffId = (int)(await insert.ExecuteScalarAsync(ct))!;
        await EnsureRoleAsync(connection, transaction, command.UserId, "Staff", ct);
        await transaction.CommitAsync(ct);
        return (await FindByIdAsync(staffId, ct))!;
    }

    public async Task<Staff> CreateAsync(CreateStaff command, string passwordHash, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        StaffSpecialty.Validate(command.StaffRole, command.IsDriver);
        await ValidateReferencesAsync(connection, transaction, command.DepartmentId, command.FacilityId, ct);
        try
        {
            var userId = await InsertUserAsync(connection, transaction, command, passwordHash, ct);
            var staffId = await InsertStaffAsync(connection, transaction, userId, command, ct);
            await EnsureRoleAsync(connection, transaction, userId, "Staff", ct);
            await SetSpecialtyAsync(connection, transaction, userId, command.IsDriver ? "Driver" : command.StaffRole, ct);
            await transaction.CommitAsync(ct);
            return (await FindByIdAsync(staffId, ct))!;
        }
        catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new StaffConflictException("Email đã được sử dụng hoặc người dùng đã là staff.");
        }
    }

    public async Task<Staff> UpdateAsync(int staffId, UpdateStaff command, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        StaffSpecialty.Validate(command.StaffRole, command.IsDriver);
        await ValidateReferencesAsync(connection, transaction, command.DepartmentId, command.FacilityId, ct);
        await using var update = new NpgsqlCommand("""
            UPDATE staffs s SET department_id = $1, facility_id = $2, hired_date = $3, is_driver = $4,
                license_number = $5, license_type = $6, license_issue_date = $7, license_expiry_date = $8, license_img = $9
            WHERE s.id = $10
            RETURNING s.user_id
            """, connection, transaction);
        AddStaffParameters(update, command);
        update.Parameters.AddWithValue(staffId);
        var userIdValue = await update.ExecuteScalarAsync(ct);
        if (userIdValue is not int userId) throw new StaffNotFoundException();
        await using var user = new NpgsqlCommand("UPDATE users SET fullname = $1, phone = $2, avatar=COALESCE($4,avatar), gender=COALESCE($5,gender), address=COALESCE($6,address) WHERE id = $3", connection, transaction);
        user.Parameters.AddWithValue(command.FullName);
        user.Parameters.AddWithValue((object?)command.Phone ?? DBNull.Value);
        user.Parameters.AddWithValue(userId);
        user.Parameters.AddWithValue((object?)command.AvatarUrl ?? DBNull.Value);
        user.Parameters.AddWithValue((object?)command.Gender ?? DBNull.Value);
        user.Parameters.AddWithValue((object?)command.Address ?? DBNull.Value);
        await user.ExecuteNonQueryAsync(ct);
        await EnsureRoleAsync(connection, transaction, userId, "Staff", ct);
        await SetSpecialtyAsync(connection, transaction, userId, command.IsDriver ? "Driver" : command.StaffRole, ct);

        await transaction.CommitAsync(ct);
        return (await FindByIdAsync(staffId, ct))!;
    }

    public async Task DeactivateAsync(int staffId, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("UPDATE users SET is_active = FALSE WHERE id = (SELECT user_id FROM staffs WHERE id = $1) AND is_active = TRUE");
        command.Parameters.AddWithValue(staffId);
        if (await command.ExecuteNonQueryAsync(ct) != 1) throw new StaffNotFoundException();
    }

    public Task<Staff> AssignDepartmentAsync(int userId, int departmentId, bool actorIsAdmin, CancellationToken ct) =>
        AssignOrganizationAsync(userId, departmentId, actorIsAdmin, false, ct);

    public Task<Staff> AssignFacilityAsync(int userId, int facilityId, bool actorIsAdmin, CancellationToken ct) =>
        AssignOrganizationAsync(userId, facilityId, actorIsAdmin, true, ct);

    private async Task<Staff> AssignOrganizationAsync(int userId, int unitId, bool actorIsAdmin, bool isFacility, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        // Serialize profile creation and membership changes for this account.
        await using var account = new NpgsqlCommand("SELECT id FROM users WHERE id=$1 AND is_active=TRUE FOR UPDATE", connection, transaction);
        account.Parameters.AddWithValue(userId);
        if (await account.ExecuteScalarAsync(ct) is null) throw new StaffNotFoundException();

        await using var db = VehicleRentalDbContext.Create(connection, transaction);
        var names = new HashSet<string>(await (from membership in db.Set<UserRolesRow>()
            join role in db.Set<RolesRow>() on membership.RoleId equals role.Id
            where membership.UserId == userId select role.Name).ToListAsync(ct), StringComparer.Ordinal);
        if (!actorIsAdmin && (names.Contains("Manager") || names.Contains("Admin")))
            throw new OrganizationAssignmentForbiddenException();
        if (!names.Contains("Manager") && !names.Contains("Staff"))
            throw new OrganizationReferenceNotFoundException("The account must already have the Staff or Manager role.");

        // SQL identifiers come only from fixed values, never request input.
        var table = isFacility ? "facilities" : "departments";
        var column = isFacility ? "facility_id" : "department_id";
        await using var unit = new NpgsqlCommand($"SELECT id FROM {table} WHERE id=$1 FOR SHARE", connection, transaction);
        unit.Parameters.AddWithValue(unitId);
        if (await unit.ExecuteScalarAsync(ct) is null)
            throw new OrganizationReferenceNotFoundException(isFacility ? "Facility was not found." : "Department was not found.");

        await using var assign = new NpgsqlCommand($"""
            INSERT INTO staffs(user_id, {column}) VALUES($1, $2)
            ON CONFLICT (user_id) DO UPDATE SET {column}=EXCLUDED.{column}
            RETURNING id
            """, connection, transaction);
        assign.Parameters.AddWithValue(userId);
        assign.Parameters.AddWithValue(unitId);
        var staffId = (int)(await assign.ExecuteScalarAsync(ct))!;
        await transaction.CommitAsync(ct);
        return (await FindByIdAsync(staffId, ct))!;
    }

    private static async Task ValidateReferencesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, int? departmentId, int? facilityId, CancellationToken ct)
    {
        if (departmentId.HasValue && !await ExistsAsync(connection, transaction, "departments", departmentId.Value, ct))
            throw new OrganizationReferenceNotFoundException("Phòng ban không tồn tại.");
        if (facilityId.HasValue && !await ExistsAsync(connection, transaction, "facilities", facilityId.Value, ct))
            throw new OrganizationReferenceNotFoundException("Cơ sở không tồn tại.");
    }

    private static async Task<bool> ExistsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string table, int id, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(connection, transaction);
        return table switch
        {
            "departments" => await db.Set<DepartmentsRow>().AnyAsync(unit => unit.Id == id, ct),
            "facilities" => await db.Set<FacilitiesRow>().AnyAsync(unit => unit.Id == id, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(table))
        };
    }

    private static async Task<int> InsertUserAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CreateStaff command, string passwordHash, CancellationToken ct)
    {
        await using var user = new NpgsqlCommand("INSERT INTO users (email, fullname, phone, password_hash, avatar, gender, address) VALUES ($1, $2, $3, $4, $5, $6, $7) RETURNING id", connection, transaction);
        user.Parameters.AddWithValue(command.Email);
        user.Parameters.AddWithValue(command.FullName);
        user.Parameters.AddWithValue((object?)command.Phone ?? DBNull.Value);
        user.Parameters.AddWithValue(passwordHash);
        user.Parameters.AddWithValue((object?)command.AvatarUrl ?? DBNull.Value);
        user.Parameters.AddWithValue((object?)command.Gender ?? DBNull.Value);
        user.Parameters.AddWithValue((object?)command.Address ?? DBNull.Value);
        return (int)(await user.ExecuteScalarAsync(ct))!;
    }

    private static async Task<int> InsertStaffAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, int userId, CreateStaff command, CancellationToken ct)
    {
        await using var staff = new NpgsqlCommand("""
            INSERT INTO staffs (user_id, department_id, facility_id, hired_date, is_driver, license_number, license_type, license_issue_date, license_expiry_date, license_img)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10) RETURNING id
            """, connection, transaction);
        staff.Parameters.AddWithValue(userId);
        AddStaffParameters(staff, command);
        return (int)(await staff.ExecuteScalarAsync(ct))!;
    }

    private static void AddStaffParameters(NpgsqlCommand command, CreateStaff staff) => AddStaffParameters(command, new UpdateStaff(staff.FullName, staff.Phone, staff.DepartmentId, staff.FacilityId, staff.HiredDate, staff.IsDriver, staff.LicenseNumber, staff.LicenseType, staff.LicenseIssueDate, staff.LicenseExpiryDate, staff.LicenseImageUrl));
    private static void AddStaffParameters(NpgsqlCommand command, UpdateStaff staff)
    {
        command.Parameters.AddWithValue((object?)staff.DepartmentId ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)staff.FacilityId ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)staff.HiredDate ?? DBNull.Value);
        command.Parameters.AddWithValue(staff.IsDriver);
        command.Parameters.AddWithValue((object?)staff.LicenseNumber ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)staff.LicenseType ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)staff.LicenseIssueDate ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)staff.LicenseExpiryDate ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)staff.LicenseImageUrl ?? DBNull.Value);
    }

    private static async Task SetSpecialtyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, int userId, string? roleName, CancellationToken ct)
    {
        await using var remove = new NpgsqlCommand("DELETE FROM user_roles WHERE user_id=$1 AND role_id IN (SELECT id FROM roles WHERE name IN ('CustomerService','Driver','Maintenance'))", connection, transaction);
        remove.Parameters.AddWithValue(userId);
        await remove.ExecuteNonQueryAsync(ct);
        if (roleName is not null) await EnsureRoleAsync(connection, transaction, userId, roleName, ct);
    }

    private static async Task EnsureRoleAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, int userId, string roleName, CancellationToken ct)
    {
        await using var create = new NpgsqlCommand("INSERT INTO roles (name) SELECT $1 WHERE NOT EXISTS (SELECT 1 FROM roles WHERE name = $1)", connection, transaction);
        create.Parameters.AddWithValue(roleName);
        await create.ExecuteNonQueryAsync(ct);
        await using var assign = new NpgsqlCommand("INSERT INTO user_roles (user_id, role_id) SELECT $1, id FROM roles WHERE name = $2 AND NOT EXISTS (SELECT 1 FROM user_roles WHERE user_id = $1 AND role_id = roles.id)", connection, transaction);
        assign.Parameters.AddWithValue(userId);
        assign.Parameters.AddWithValue(roleName);
        await assign.ExecuteNonQueryAsync(ct);
    }

}
