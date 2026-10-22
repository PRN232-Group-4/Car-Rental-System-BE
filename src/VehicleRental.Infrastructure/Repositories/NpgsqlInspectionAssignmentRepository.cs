using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.Inspections;
using Car_Rental_System_BE.Application.Lifecycle;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Repositories;

public sealed class NpgsqlInspectionAssignmentRepository(NpgsqlDataSource dataSource) : IInspectionAssignmentRepository
{
    public async Task<InspectionAssignment?> GetAsync(int rentalId, CancellationToken ct, string inspectionType = "pre_rental")
    {
        await using var command = dataSource.CreateCommand("""
            SELECT r.vehicle_id, a.technician_staff_id, COALESCE(u.fullname,u.email), a.assigned_at, a.assigned_by, u.email
            FROM rentals r
            LEFT JOIN inspection_assignments a ON a.rental_id=r.id AND a.vehicle_id=r.vehicle_id AND a.inspection_type=$2
            LEFT JOIN staffs s ON s.id=a.technician_staff_id
            LEFT JOIN users u ON u.id=s.user_id
            WHERE r.id=$1
            """);
        command.Parameters.AddWithValue(rentalId);
        command.Parameters.AddWithValue(inspectionType);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new NotFoundException("RENTAL_NOT_FOUND", "Không tìm thấy đơn thuê.");
        return reader.IsDBNull(1) ? null : new(rentalId, reader.GetInt32(0), reader.GetInt32(1),
            reader.IsDBNull(2) ? "" : reader.GetString(2), reader.GetDateTime(3), reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5));
    }

    public Task<InspectionAssignment> AssignAsync(int rentalId, int technicianStaffId, string assignedBy, CancellationToken ct, string inspectionType = "pre_rental") =>
        SaveAsync(rentalId, technicianStaffId, assignedBy, false, false, inspectionType, ct);

    public Task<InspectionAssignment> ConfirmCheckedAsync(int rentalId, int technicianStaffId, string confirmedBy, CancellationToken ct, bool approveRequest = false, string inspectionType = "pre_rental") =>
        SaveAsync(rentalId, technicianStaffId, confirmedBy, true, approveRequest, inspectionType, ct);

    private async Task<InspectionAssignment> SaveAsync(int rentalId, int technicianStaffId, string assignedBy, bool checkedByAdmin, bool approveRequest, string inspectionType, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        // Share the dispatcher lock with inspection creation and vehicle replacement.
        await using (var operationLock = new NpgsqlCommand("SELECT pg_advisory_xact_lock(724232001::bigint)", connection, transaction))
            await operationLock.ExecuteNonQueryAsync(ct);
        var post = inspectionType == "post_rental";
        int vehicleId;
        string status;
        bool approved;
        await using (var rental = new NpgsqlCommand("SELECT vehicle_id,status,started_at,confirmed_at FROM rentals WHERE id=$1 FOR UPDATE", connection, transaction))
        {
            rental.Parameters.AddWithValue(rentalId);
            await using var reader = await rental.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) throw new NotFoundException("RENTAL_NOT_FOUND", "Không tìm thấy đơn thuê.");
            if (post ? reader.GetString(1) != "post_inspection" : reader.GetString(1) is not ("pending" or "pre_inspection") || !reader.IsDBNull(2))
                throw new ConflictException("INSPECTION_ASSIGNMENT_NOT_ALLOWED", "Chỉ phân công kiểm tra ở đúng giai đoạn trước thuê hoặc sau trả xe.");
            vehicleId = reader.GetInt32(0);
            status = reader.GetString(1);
            approved = !reader.IsDBNull(3);
        }
        string technicianName;
        string technicianEmail;
        await using (var technician = new NpgsqlCommand("""
            SELECT COALESCE(u.fullname,u.email),u.email FROM staffs s JOIN users u ON u.id=s.user_id
            WHERE s.id=$1 AND u.is_active AND EXISTS (
                SELECT 1 FROM user_roles ur JOIN roles r ON r.id=ur.role_id WHERE ur.user_id=u.id AND r.name='Maintenance')
            FOR SHARE OF s,u
            """, connection, transaction))
        {
            technician.Parameters.AddWithValue(technicianStaffId);
            await using var reader = await technician.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                throw new BusinessRuleException("TECHNICIAN_NOT_ELIGIBLE", "Hãy chọn kỹ thuật viên bảo trì đang hoạt động.");
            technicianName = reader.GetString(0);
            technicianEmail = reader.GetString(1);
        }
        await using (var evidence = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM inspections WHERE rental_id=$1 AND vehicle_id=$2 AND inspection_type=$3)", connection, transaction))
        {
            evidence.Parameters.AddWithValue(rentalId);
            evidence.Parameters.AddWithValue(vehicleId);
            evidence.Parameters.AddWithValue(inspectionType);
            if (await evidence.ExecuteScalarAsync(ct) is true)
                throw new ConflictException("INSPECTION_ALREADY_RECORDED", "Xe đã có biên bản ở giai đoạn kiểm tra này; không thể ghi đè hoặc đổi phân công.");
        }
        await using var save = new NpgsqlCommand("""
            INSERT INTO inspection_assignments(rental_id,vehicle_id,technician_staff_id,assigned_by,inspection_type)
            VALUES($1,$2,$3,$4,$5)
            ON CONFLICT(rental_id,vehicle_id,inspection_type) DO UPDATE SET
                technician_staff_id=EXCLUDED.technician_staff_id,assigned_by=EXCLUDED.assigned_by,
                assigned_at=(now() AT TIME ZONE 'utc')
            RETURNING assigned_at
            """, connection, transaction);
        save.Parameters.AddWithValue(rentalId);
        save.Parameters.AddWithValue(vehicleId);
        save.Parameters.AddWithValue(technicianStaffId);
        save.Parameters.AddWithValue(assignedBy);
        save.Parameters.AddWithValue(inspectionType);
        var assignedAt = (DateTime)(await save.ExecuteScalarAsync(ct))!;
        if (checkedByAdmin)
        {
            await using var check = new NpgsqlCommand("""
                INSERT INTO inspections(rental_id,vehicle_id,staff_id,inspection_type,is_ok,inspected_at,damage_notes,confirmation_source,confirmed_by)
                VALUES($1,$2,$3,$5,true,now() AT TIME ZONE 'utc',
                    'Admin xác nhận đã phân công và kiểm tra đạt.','admin_confirmation',$4)
                """, connection, transaction);
            check.Parameters.AddWithValue(rentalId);
            check.Parameters.AddWithValue(vehicleId);
            check.Parameters.AddWithValue(technicianStaffId);
            check.Parameters.AddWithValue(assignedBy);
            check.Parameters.AddWithValue(inspectionType);
            await check.ExecuteNonQueryAsync(ct);
            if (!post && approveRequest && (status == "pending" || !approved))
                await RentalApprovalWriter.WriteAsync(connection, transaction, rentalId, status, assignedAt,
                    new ActorContext(null, assignedBy, "Admin"), "Admin approved request with passing pre-rental inspection", ct);
        }
        await transaction.CommitAsync(ct);
        return new(rentalId, vehicleId, technicianStaffId, technicianName, assignedAt, assignedBy, technicianEmail);
    }
}
