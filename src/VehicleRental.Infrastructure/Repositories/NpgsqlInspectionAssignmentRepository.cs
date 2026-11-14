using Microsoft.EntityFrameworkCore;
using Car_Rental_System_BE.Infrastructure.Persistence;
using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.Features.Inspections;
using Car_Rental_System_BE.Application.Features.Rentals;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Repositories;

public sealed class NpgsqlInspectionAssignmentRepository(NpgsqlDataSource dataSource) : IInspectionAssignmentRepository
{
    public async Task<InspectionAssignment?> GetAsync(int rentalId, CancellationToken ct, string inspectionType = "pre_rental")
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        var row = await (from rental in db.Set<RentalsRow>()
            from assignment in db.Set<InspectionAssignmentsRow>().Where(a => a.RentalId == rental.Id &&
                a.VehicleId == rental.VehicleId && a.InspectionType == inspectionType).DefaultIfEmpty()
            from staff in db.Set<StaffsRow>().Where(s => s.Id == assignment.TechnicianStaffId).DefaultIfEmpty()
            from user in db.Set<UsersRow>().Where(u => u.Id == staff.UserId).DefaultIfEmpty()
            where rental.Id == rentalId
            select new { rental.VehicleId, TechnicianId = (int?)assignment.TechnicianStaffId,
                Name = user.Fullname ?? user.Email, AssignedAt = (DateTime?)assignment.AssignedAt,
                assignment.AssignedBy, user.Email }).FirstOrDefaultAsync(ct);
        if (row is null) throw new NotFoundException("RENTAL_NOT_FOUND", "Không tìm thấy đơn thuê.");
        return row.TechnicianId is null ? null : new(rentalId, row.VehicleId, row.TechnicianId.Value,
            row.Name ?? "", row.AssignedAt!.Value, row.AssignedBy, row.Email);
    }

    public Task<InspectionAssignment> AssignAsync(int rentalId, int technicianStaffId, string assignedBy, CancellationToken ct, string inspectionType = "pre_rental") =>
        SaveAsync(rentalId, technicianStaffId, assignedBy, false, false, inspectionType, ct);

    public Task<InspectionAssignment> ConfirmCheckedAsync(int rentalId, int technicianStaffId, string confirmedBy, CancellationToken ct, bool approveRequest = false, string inspectionType = "pre_rental") =>
        SaveAsync(rentalId, technicianStaffId, confirmedBy, true, approveRequest, inspectionType, ct);

    private async Task<InspectionAssignment> SaveAsync(int rentalId, int technicianStaffId, string assignedBy, bool checkedByAdmin, bool approveRequest, string inspectionType, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var db = VehicleRentalDbContext.Create(connection, transaction);
        // Dùng chung khóa điều phối với việc tạo biên bản kiểm tra và thay xe.
        await db.AdvisoryLockAsync(724232001L, ct);
        var post = inspectionType == "post_rental";
        var rental = await db.LockByKeyAsync<RentalsRow>(rentalId, ct)
            ?? throw new NotFoundException("RENTAL_NOT_FOUND", "Không tìm thấy đơn thuê.");
        if (post ? rental.Status != "post_inspection" : rental.Status is not ("pending" or "pre_inspection") || rental.StartedAt is not null)
            throw new ConflictException("INSPECTION_ASSIGNMENT_NOT_ALLOWED", "Chỉ phân công kiểm tra ở đúng giai đoạn trước thuê hoặc sau trả xe.");
        var vehicleId = rental.VehicleId;
        var status = rental.Status;
        var approved = rental.ConfirmedAt is not null;

        // FOR SHARE OF staffs, users: không thể vô hiệu hóa hay đổi vai trò kỹ thuật viên cho tới khi commit.
        var staff = await db.LockByKeyAsync<StaffsRow>(technicianStaffId, ct, RowLock.Share);
        var user = staff is null ? null : await db.LockByKeyAsync<UsersRow>(staff.UserId, ct, RowLock.Share);
        if (user is null || !user.IsActive || !await (from ur in db.Set<UserRolesRow>() join r in db.Set<RolesRow>() on ur.RoleId equals r.Id
                where ur.UserId == user.Id && r.Name == "Maintenance" select ur.Id).AnyAsync(ct))
            throw new BusinessRuleException("TECHNICIAN_NOT_ELIGIBLE", "Hãy chọn kỹ thuật viên bảo trì đang hoạt động.");
        var technicianName = user.Fullname ?? user.Email;
        var technicianEmail = user.Email;
        if (await db.Set<InspectionsRow>().AnyAsync(i => i.RentalId == rentalId && i.VehicleId == vehicleId &&
                i.InspectionType == inspectionType, ct))
            throw new ConflictException("INSPECTION_ALREADY_RECORDED", "Xe đã có biên bản ở giai đoạn kiểm tra này; không thể ghi đè hoặc đổi phân công.");

        // Khóa điều phối + khóa dòng đơn thuê tuần tự hóa các luồng ghi, nên cập nhật-hoặc-chèn thay cho ON CONFLICT DO UPDATE.
        var utcNow = await db.DatabaseUtcNowAsync(ct);
        var assignment = db.Set<InspectionAssignmentsRow>().Where(a => a.RentalId == rentalId && a.VehicleId == vehicleId && a.InspectionType == inspectionType);
        if (await assignment.ExecuteUpdateAsync(s => s
                .SetProperty(a => a.TechnicianStaffId, technicianStaffId)
                .SetProperty(a => a.AssignedBy, assignedBy)
                .SetProperty(a => a.AssignedAt, utcNow), ct) == 0)
            await db.InsertAsync(new InspectionAssignmentsRow
            {
                RentalId = rentalId, VehicleId = vehicleId, TechnicianStaffId = technicianStaffId, AssignedBy = assignedBy, InspectionType = inspectionType,
            }, ct);
        var assignedAt = await assignment.Select(a => a.AssignedAt).SingleAsync(ct);
        if (checkedByAdmin)
        {
            await db.InsertAsync(new InspectionsRow
            {
                RentalId = rentalId, VehicleId = vehicleId, StaffId = technicianStaffId, InspectionType = inspectionType, IsOk = true,
                InspectedAt = utcNow, DamageNotes = "Admin xác nhận đã phân công và kiểm tra đạt.",
                ConfirmationSource = "admin_confirmation", ConfirmedBy = assignedBy,
            }, ct);
            if (!post && approveRequest && (status == "pending" || !approved))
                await RentalApprovalWriter.WriteAsync(connection, transaction, rentalId, status, assignedAt,
                    new ActorContext(null, assignedBy, "Admin"), "Admin approved request with passing pre-rental inspection", ct);
        }
        await transaction.CommitAsync(ct);
        return new(rentalId, vehicleId, technicianStaffId, technicianName, assignedAt, assignedBy, technicianEmail);
    }
}
