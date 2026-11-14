using Car_Rental_System_BE.Application.DTOs.Operations;

namespace Car_Rental_System_BE.Application.Features.RentalPolicies;

/// <summary>
/// Các phiên bản chính sách thuê: tạo mới sau khi kiểm tra loại xe và phạm vi, chuyển phiên bản đang áp dụng,
/// và danh sách phân trang kèm số lượt sử dụng. Ba thành viên này từng nằm trong <c>OperationsService</c>; các
/// quy tắc và thứ tự giữ nguyên khi chuyển sang.
/// </summary>
public sealed class RentalPolicyService(IRentalPolicyRepository repository) : IRentalPolicyService
{
    public async Task<RentalPolicy> CreatePolicyAsync(RentalPolicy policy, CancellationToken ct)
    {
        RentalPolicyValidator.Validate(policy);
        if (policy.VehicleTypeId is { } typeId && !await repository.VehicleTypeExistsAsync(typeId, ct))
            throw RentalPolicyValidator.InvalidVehicleType();
        if (await repository.HasActivePolicyAsync(policy.VehicleTypeId, ct))
            throw RentalPolicyValidator.ActivePolicyExists();
        return await repository.CreatePolicyAsync(policy, ct);
    }

    public async Task<RentalPolicy> SetPolicyStatusAsync(int id, bool isActive, CancellationToken ct)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id), "Policy ID must be positive.");
        return await repository.SetPolicyStatusAsync(id, isActive, ct)
            ?? throw new KeyNotFoundException("Rental policy was not found.");
    }

    public async Task DeletePolicyAsync(int id, CancellationToken ct)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id), "Policy ID must be positive.");
        if (!await repository.DeletePolicyAsync(id, ct))
            throw new KeyNotFoundException("Rental policy was not found.");
    }

    public Task<RentalPolicyPage> ListPoliciesAsync(int page, int pageSize, CancellationToken ct)
    {
        RentalPolicyValidator.ValidatePagination(page, pageSize);
        return repository.ListPoliciesAsync(page, pageSize, ct);
    }
}
