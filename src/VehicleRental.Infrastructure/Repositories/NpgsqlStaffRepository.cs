using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.DTOs.Organization;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Car_Rental_System_BE.Domain.Services;
using Car_Rental_System_BE.Application.Validators;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Repositories;

public sealed class NpgsqlStaffRepository(NpgsqlDataSource dataSource) : IStaffRepository
{
    private const string StaffSelect = """
        SELECT s.id, u.id, u.email, COALESCE(u.fullname, ''), u.phone,
               s.department_id, d.name, s.facility_id, f.name, s.hired_date,
               EXISTS(SELECT 1 FROM user_roles dr JOIN roles rr ON rr.id=dr.role_id WHERE dr.user_id=u.id AND rr.name='Driver'),
               s.license_number, s.license_type, s.license_issue_date, s.license_expiry_date,
               s.license_img, s.rating, u.is_active,
               (SELECT r.name FROM user_roles ur JOIN roles r ON r.id=ur.role_id WHERE ur.user_id=u.id AND r.name IN ('CustomerService','Driver','Maintenance') ORDER BY r.name LIMIT 1),
               u.avatar, u.gender, u.address,
               (SELECT COUNT(DISTINCT a.rental_id)::integer FROM vehicle_assignments a WHERE a.driver_staff_id=s.id),
               (SELECT a.rental_id FROM vehicle_assignments a JOIN rentals r ON r.id=a.rental_id
                WHERE a.driver_staff_id=s.id AND a.status='assigned' AND r.status='renting' ORDER BY a.assigned_at DESC,a.id DESC LIMIT 1),
               ARRAY(SELECT DISTINCT rr.name FROM user_roles ur JOIN roles rr ON rr.id=ur.role_id WHERE ur.user_id=u.id ORDER BY rr.name)
        FROM staffs s
        JOIN users u ON u.id = s.user_id
        LEFT JOIN departments d ON d.id = s.department_id
        LEFT JOIN facilities f ON f.id = s.facility_id
        """;

    public async Task<StaffPage> SearchAsync(string? search, bool? isDriver, int page, int pageSize, CancellationToken ct)
    {
        const string filter = """

            WHERE u.is_active = TRUE
              AND ($1::boolean IS NULL OR EXISTS(SELECT 1 FROM user_roles dr JOIN roles rr ON rr.id=dr.role_id WHERE dr.user_id=u.id AND rr.name='Driver') = $1::boolean)
              AND ($2::text IS NULL OR u.email ILIKE '%' || $2 || '%' OR u.fullname ILIKE '%' || $2 || '%'
                   OR u.phone ILIKE '%' || $2 || '%' OR s.license_number ILIKE '%' || $2 || '%')
            """;
        var normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        await using var count = dataSource.CreateCommand("SELECT COUNT(*) FROM staffs s JOIN users u ON u.id = s.user_id " + filter);
        count.Parameters.AddWithValue((object?)isDriver ?? DBNull.Value);
        count.Parameters.AddWithValue((object?)normalizedSearch ?? DBNull.Value);
        var total = Convert.ToInt32(await count.ExecuteScalarAsync(ct));

        await using var command = dataSource.CreateCommand(StaffSelect + filter + " ORDER BY u.fullname, s.id LIMIT $3 OFFSET $4");
        command.Parameters.AddWithValue((object?)isDriver ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)normalizedSearch ?? DBNull.Value);
        command.Parameters.AddWithValue(pageSize);
        command.Parameters.AddWithValue(((long)page - 1) * pageSize);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var items = new List<Staff>();
        while (await reader.ReadAsync(ct)) items.Add(Read(reader));
        return new StaffPage(items, page, pageSize, total);
    }

    public async Task<Staff?> FindByIdAsync(int staffId, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand(StaffSelect + " WHERE s.id = $1");
        command.Parameters.AddWithValue(staffId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
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
        await using (var existing = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM staffs WHERE user_id=$1)", connection, transaction))
        {
            existing.Parameters.AddWithValue(command.UserId);
            if (await existing.ExecuteScalarAsync(ct) is true)
                throw new StaffConflictException("Tài khoản đã có hồ sơ nhân viên. Hãy mở hồ sơ để cập nhật.");
        }
        await ValidateReferencesAsync(connection, transaction, command.DepartmentId, command.FacilityId, ct);
        bool isDriver;
        await using (var role = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM user_roles ur JOIN roles r ON r.id=ur.role_id WHERE ur.user_id=$1 AND r.name='Driver')", connection, transaction))
        {
            role.Parameters.AddWithValue(command.UserId);
            isDriver = await role.ExecuteScalarAsync(ct) is true;
        }
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

        await using var roles = new NpgsqlCommand("SELECT r.name FROM user_roles ur JOIN roles r ON r.id=ur.role_id WHERE ur.user_id=$1", connection, transaction);
        roles.Parameters.AddWithValue(userId);
        var names = new HashSet<string>(StringComparer.Ordinal);
        await using (var reader = await roles.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) names.Add(reader.GetString(0));
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
        await using var command = new NpgsqlCommand($"SELECT EXISTS (SELECT 1 FROM {table} WHERE id = $1)", connection, transaction);
        command.Parameters.AddWithValue(id);
        return (bool)(await command.ExecuteScalarAsync(ct))!;
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

    private static Staff Read(NpgsqlDataReader reader) => new(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3), NullableString(reader, 4), NullableInt(reader, 5), NullableString(reader, 6), NullableInt(reader, 7), NullableString(reader, 8), NullableDate(reader, 9), reader.GetBoolean(10), NullableString(reader, 11), NullableString(reader, 12), NullableDate(reader, 13), NullableDate(reader, 14), NullableString(reader, 15), reader.IsDBNull(16) ? null : reader.GetDecimal(16), reader.GetBoolean(17), NullableString(reader, 18), NullableString(reader, 19), NullableString(reader, 20), NullableString(reader, 21), reader.GetInt32(22), NullableInt(reader, 23)) { Roles = reader.GetFieldValue<string[]>(24) };
    private static string? NullableString(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static int? NullableInt(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    private static DateOnly? NullableDate(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateOnly>(ordinal);
}
