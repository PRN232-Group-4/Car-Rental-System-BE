using System.ComponentModel.DataAnnotations;
using Car_Rental_System_BE.Application.Common.Errors;

namespace Car_Rental_System_BE.Application.Inspections;

public sealed record InspectionAssignment(
    int RentalId, int VehicleId, int TechnicianStaffId, string TechnicianName,
    DateTime AssignedAt, string AssignedBy, string? TechnicianEmail = null);

public sealed record AssignInspectionRequest(
    [param: Required, Range(1, int.MaxValue)] int? TechnicianStaffId);

public interface IInspectionAssignmentRepository
{
    Task<InspectionAssignment?> GetAsync(int rentalId, CancellationToken ct);
    Task<InspectionAssignment> AssignAsync(int rentalId, int technicianStaffId, string assignedBy, CancellationToken ct);
    Task<InspectionAssignment> ConfirmCheckedAsync(int rentalId, int technicianStaffId, string confirmedBy, CancellationToken ct);
}

public interface IInspectionAssignmentService
{
    Task<InspectionAssignment?> GetAsync(int rentalId, CancellationToken ct);
    Task<InspectionAssignment> AssignAsync(int rentalId, int technicianStaffId, string assignedBy, CancellationToken ct);
    Task<InspectionAssignment> ConfirmCheckedAsync(int rentalId, int technicianStaffId, string confirmedBy, CancellationToken ct);
}

public sealed class InspectionAssignmentService(IInspectionAssignmentRepository repository) : IInspectionAssignmentService
{
    public Task<InspectionAssignment?> GetAsync(int rentalId, CancellationToken ct)
    {
        ValidateId(rentalId, "RENTAL_ID_INVALID");
        return repository.GetAsync(rentalId, ct);
    }

    public Task<InspectionAssignment> AssignAsync(int rentalId, int technicianStaffId, string assignedBy, CancellationToken ct)
    {
        ValidateId(rentalId, "RENTAL_ID_INVALID");
        ValidateId(technicianStaffId, "TECHNICIAN_ID_INVALID");
        if (string.IsNullOrWhiteSpace(assignedBy))
            throw new BusinessRuleException("ACTOR_REQUIRED", "Người phân công không hợp lệ.");
        return repository.AssignAsync(rentalId, technicianStaffId, assignedBy, ct);
    }

    private static void ValidateId(int id, string code)
    {
        if (id <= 0) throw new BusinessRuleException(code, "Mã đơn thuê và kỹ thuật viên phải lớn hơn 0.");
    }

    public Task<InspectionAssignment> ConfirmCheckedAsync(int rentalId, int technicianStaffId, string confirmedBy, CancellationToken ct)
    {
        ValidateId(rentalId, "RENTAL_ID_INVALID");
        ValidateId(technicianStaffId, "TECHNICIAN_ID_INVALID");
        if (string.IsNullOrWhiteSpace(confirmedBy))
            throw new BusinessRuleException("ACTOR_REQUIRED", "Người xác nhận không hợp lệ.");
        return repository.ConfirmCheckedAsync(rentalId, technicianStaffId, confirmedBy, ct);
    }
}
