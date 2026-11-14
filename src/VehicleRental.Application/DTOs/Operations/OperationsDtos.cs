namespace Car_Rental_System_BE.Application.DTOs.Operations;

public sealed record Rental(int Id, int CustomerId, int VehicleId, string RentalType, string Status, bool IsSubscription, decimal VatPrice, decimal DepositPrice, decimal OvertimePrice, decimal TotalPrice, DateTime? StartTime, DateTime? EndTime, decimal? HourlyPriceSnapshot = null, decimal? MonthlyPriceSnapshot = null, bool? HasDriver = null, string? ContractLink = null, DateTime? SignedAt = null, int? OrderId = null, bool HasPassingPreInspection = false, bool HasPostInspection = false)
{
    // Only the atomic upload writer records this relative URL; the legacy endpoint accepts absolute HTTP(S) URLs.
    // The handover invariant independently checks the persisted private evidence under the rental lock.
    public bool HasUploadedContract => RentalType == "longterm" && SignedAt is not null && ContractLink == $"/api/rental/{Id}/contract/document";
}
