using Car_Rental_System_BE.Application.DTOs.Operations;

namespace Car_Rental_System_BE.Application.Features.RentalPolicies;

/// <summary>
/// Các phiên bản chính sách thuê: điều khoản có giá mà đơn thuê chụp lại khi đặt, và số lượt sử dụng của
/// từng phiên bản. Năm thành viên này từng nằm trên <c>IOperationsRepository</c>/<c>IOperationsService</c>
/// cùng phần còn lại của nghiệp vụ vận hành.
/// </summary>
public interface IRentalPolicyRepository
{
    Task<RentalPolicy> CreatePolicyAsync(RentalPolicy policy, CancellationToken ct);
    Task<bool> VehicleTypeExistsAsync(int id, CancellationToken ct);
    Task<bool> HasActivePolicyAsync(int? vehicleTypeId, CancellationToken ct);
    Task<RentalPolicy?> SetPolicyStatusAsync(int id, bool isActive, CancellationToken ct);
    Task<RentalPolicyPage> ListPoliciesAsync(int page, int pageSize, CancellationToken ct);
    /// <summary>Xóa một phiên bản chính sách chưa có đơn thuê nào dùng; trả về false khi chính sách không tồn tại.</summary>
    Task<bool> DeletePolicyAsync(int id, CancellationToken ct);
}

public interface IRentalPolicyService
{
    Task<RentalPolicy> CreatePolicyAsync(RentalPolicy policy, CancellationToken ct);
    Task<RentalPolicy> SetPolicyStatusAsync(int id, bool isActive, CancellationToken ct);
    Task DeletePolicyAsync(int id, CancellationToken ct);
    Task<RentalPolicyPage> ListPoliciesAsync(int page, int pageSize, CancellationToken ct);
}
