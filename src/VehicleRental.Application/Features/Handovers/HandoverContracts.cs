using Car_Rental_System_BE.Application.Features.Rentals;

namespace Car_Rental_System_BE.Application.Features.Handovers;

public sealed record HandoverFilter(string Status = "pending", int? CustomerId = null, int? VehicleId = null,
    DateTime? From = null, DateTime? To = null, int Page = 1, int PageSize = 20);

public sealed record HandoverSummary(OperationalRentalSummary Rental, string Status);
public sealed record HandoverDetail(OperationalRentalDetail Rental, string Status);
public sealed record HandoverConfirmation(int RentalId, string Status, string RentalStatus);

public interface IHandoverService
{
    Task<PagedResult<HandoverSummary>> ListAsync(HandoverFilter filter, CancellationToken ct);
    Task<HandoverDetail> GetAsync(int rentalId, CancellationToken ct);
    Task<HandoverConfirmation> ConfirmAsync(int rentalId, ActorContext actor, CancellationToken ct);
}
