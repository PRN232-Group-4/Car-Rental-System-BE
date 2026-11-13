using Car_Rental_System_BE.Application.DTOs.Operations;

namespace Car_Rental_System_BE.Application.Features.RentalPolicies;

/// <summary>
/// Rental policy versions: the priced terms a rental snapshots at booking time, and the usage count each
/// version carries. These five members used to sit on <c>IOperationsRepository</c>/<c>IOperationsService</c>
/// with the rest of the operations work.
/// </summary>
public interface IRentalPolicyRepository
{
    Task<RentalPolicy> CreatePolicyAsync(RentalPolicy policy, CancellationToken ct);
    Task<bool> VehicleTypeExistsAsync(int id, CancellationToken ct);
    Task<bool> HasActivePolicyAsync(int? vehicleTypeId, CancellationToken ct);
    Task<RentalPolicy?> SetPolicyStatusAsync(int id, bool isActive, CancellationToken ct);
    Task<RentalPolicyPage> ListPoliciesAsync(int page, int pageSize, CancellationToken ct);
}

public interface IRentalPolicyService
{
    Task<RentalPolicy> CreatePolicyAsync(RentalPolicy policy, CancellationToken ct);
    Task<RentalPolicy> SetPolicyStatusAsync(int id, bool isActive, CancellationToken ct);
    Task<RentalPolicyPage> ListPoliciesAsync(int page, int pageSize, CancellationToken ct);
}
