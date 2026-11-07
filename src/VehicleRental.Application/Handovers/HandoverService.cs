using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.Lifecycle;
using Car_Rental_System_BE.Domain.Lifecycle;

namespace Car_Rental_System_BE.Application.Handovers;

public sealed class HandoverService(IRentalQueries queries, IRentalLifecycleService lifecycle) : IHandoverService
{
    public async Task<PagedResult<HandoverSummary>> ListAsync(HandoverFilter filter, CancellationToken ct)
    {
        if (filter.Status is not ("pending" or "completed" or "all"))
            throw new BusinessRuleException("INVALID_HANDOVER_STATUS", "Status must be pending, completed or all.");
        if (filter.Page < 1 || filter.PageSize is < 1 or > 100)
            throw new BusinessRuleException("INVALID_PAGINATION", "Page must be at least 1 and pageSize must be between 1 and 100.");
        if (filter.CustomerId is <= 0 || filter.VehicleId is <= 0 || filter.From > filter.To)
            throw new BusinessRuleException("INVALID_HANDOVER_FILTER", "IDs must be positive and from cannot be later than to.");

        var page = await queries.ListOperationalRentalsAsync(new OperationalFilter(null,
            filter.CustomerId, filter.VehicleId, filter.From, filter.To, filter.Page, filter.PageSize, filter.Status), ct);
        return new PagedResult<HandoverSummary>(page.Items.Select(x => new HandoverSummary(x,
                x.Status == "pre_inspection" ? "pending" : "completed")).ToArray(),
            page.TotalCount, page.Page, page.PageSize);
    }

    public async Task<HandoverDetail> GetAsync(int rentalId, CancellationToken ct)
    {
        ValidateId(rentalId);
        var detail = await queries.GetOperationalDetailAsync(rentalId, ct);
        if (detail is null || (detail.Rental.Status != RentalStatus.PreInspection &&
            !(detail.Rental.StartedAt is not null && detail.Rental.Status is RentalStatus.Renting or RentalStatus.PostInspection or RentalStatus.Completed)))
            throw new NotFoundException("HANDOVER_NOT_FOUND", "Handover was not found.");
        return new HandoverDetail(detail, detail.Rental.Status == RentalStatus.PreInspection ? "pending" : "completed");
    }

    public async Task<HandoverConfirmation> ConfirmAsync(int rentalId, ActorContext actor, CancellationToken ct)
    {
        ValidateId(rentalId);
        var result = await lifecycle.StartAsync(rentalId, actor, ct);
        return new HandoverConfirmation(result.RentalId, "completed", result.ToStatus);
    }

    private static void ValidateId(int rentalId)
    {
        if (rentalId < 1) throw new BusinessRuleException("INVALID_RENTAL_ID", "Rental ID must be positive.");
    }
}
