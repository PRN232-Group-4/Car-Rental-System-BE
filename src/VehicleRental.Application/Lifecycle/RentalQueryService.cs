using Car_Rental_System_BE.Application.Common.Errors;

namespace Car_Rental_System_BE.Application.Lifecycle;

/// <summary>Persistence port; HTTP controllers depend on IRentalQueries instead.</summary>
public interface IRentalQueryRepository
{
    Task<PagedResult<CustomerRentalSummary>> ListMyRentalsAsync(int customerId, string? tab, int page, int pageSize, CancellationToken ct);
    Task<CustomerRentalDetail?> GetMyRentalDetailAsync(int rentalId, int customerId, CancellationToken ct);
    Task<PagedResult<OperationalRentalSummary>> ListOperationalRentalsAsync(OperationalFilter filter, CancellationToken ct);
    Task<OperationalRentalDetail?> GetOperationalDetailAsync(int rentalId, CancellationToken ct);
    Task<IReadOnlyList<OverdueRentalItem>> ListOverdueRentalsAsync(CancellationToken ct);
}

public static class RentalQueryValidation
{
    public static void Pagination(int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > 100)
            throw new BusinessRuleException("INVALID_PAGINATION", "page must be at least 1 and pageSize must be between 1 and 100.");
    }

    public static void CustomerTab(string? tab)
    {
        if (!string.IsNullOrWhiteSpace(tab) && tab.Trim().ToLowerInvariant() is not ("upcoming" or "ongoing" or "completed" or "cancelled"))
            throw new BusinessRuleException("INVALID_RENTAL_TAB", "tab must be upcoming, ongoing, completed or cancelled.");
    }

    public static void Operational(OperationalFilter filter)
    {
        Pagination(filter.Page, filter.PageSize);
        if (filter.CustomerId is <= 0 || filter.VehicleId is <= 0)
            throw new BusinessRuleException("INVALID_RENTAL_FILTER", "customerId and vehicleId must be positive.");
        if (filter.From > filter.To)
            throw new BusinessRuleException("INVALID_RENTAL_PERIOD", "from must not be later than to.");
        if (!string.IsNullOrWhiteSpace(filter.Status) && filter.Status.Trim().ToLowerInvariant() is not
            ("pending" or "pre_inspection" or "renting" or "post_inspection" or "completed" or "cancelled"))
            throw new BusinessRuleException("INVALID_RENTAL_STATUS", "Unknown rental status.");
        if (filter.HandoverStatus is not (null or "pending" or "completed" or "all"))
            throw new BusinessRuleException("INVALID_HANDOVER_STATUS", "handover status must be pending, completed or all.");
    }
}

public sealed class RentalQueryService(IRentalQueryRepository repository) : IRentalQueries
{
    public Task<PagedResult<CustomerRentalSummary>> ListMyRentalsAsync(int customerId, string? tab, int page, int pageSize, CancellationToken ct)
    {
        RentalQueryValidation.Pagination(page, pageSize);
        RentalQueryValidation.CustomerTab(tab);
        return repository.ListMyRentalsAsync(customerId, tab, page, pageSize, ct);
    }

    public Task<PagedResult<OperationalRentalSummary>> ListOperationalRentalsAsync(OperationalFilter filter, CancellationToken ct)
    {
        RentalQueryValidation.Operational(filter);
        return repository.ListOperationalRentalsAsync(filter, ct);
    }

    public Task<CustomerRentalDetail?> GetMyRentalDetailAsync(int rentalId, int customerId, CancellationToken ct) => repository.GetMyRentalDetailAsync(rentalId, customerId, ct);
    public Task<OperationalRentalDetail?> GetOperationalDetailAsync(int rentalId, CancellationToken ct) => repository.GetOperationalDetailAsync(rentalId, ct);
    public Task<IReadOnlyList<OverdueRentalItem>> ListOverdueRentalsAsync(CancellationToken ct) => repository.ListOverdueRentalsAsync(ct);
}
