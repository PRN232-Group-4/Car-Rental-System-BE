using Car_Rental_System_BE.Application.DTOs.Operations;

namespace Car_Rental_System_BE.Application.Features.RentalPolicies;

/// <summary>
/// Rental policy versions: creating one after checking its vehicle type and scope, switching the active
/// version, and the paged list with usage counts. The three members sat in <c>OperationsService</c>; the rules
/// and their order are the same ones, moved unchanged.
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

    public Task<RentalPolicyPage> ListPoliciesAsync(int page, int pageSize, CancellationToken ct)
    {
        RentalPolicyValidator.ValidatePagination(page, pageSize);
        return repository.ListPoliciesAsync(page, pageSize, ct);
    }
}
