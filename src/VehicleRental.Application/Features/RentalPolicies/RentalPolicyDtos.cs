using System.ComponentModel.DataAnnotations;

namespace Car_Rental_System_BE.Application.Features.RentalPolicies;

public sealed record RentalPolicy(int Id, int? VehicleTypeId, string Name, decimal VatRate, decimal OvertimeFeeRate, decimal DepositPercentage, bool IsActive, long RentalCount = 0, string? VehicleTypeName = null, int? ManagedBy = null, DateTime? CreatedAt = null);
public sealed record RentalPolicyPage(IReadOnlyList<RentalPolicy> Items, int Page, int PageSize, int TotalCount);
public sealed record PolicyRequest([param:Range(1,int.MaxValue)]int? VehicleTypeId,[param:Required] string Name,[param:Range(0,100)]decimal VatRate,[param:Range(0,100)]decimal OvertimeFeeRate,[param:Range(0,100)]decimal DepositPercentage,bool IsActive=true){public RentalPolicy ToModel()=>new(0,VehicleTypeId,Name,VatRate,OvertimeFeeRate,DepositPercentage,IsActive);}
public sealed record PolicyStatusRequest
{
    [Required]
    public bool? IsActive { get; init; }
}
