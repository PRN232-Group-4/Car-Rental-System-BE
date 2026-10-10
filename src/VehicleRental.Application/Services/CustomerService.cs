using Car_Rental_System_BE.Application.DTOs.Customers;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Car_Rental_System_BE.Application.Interfaces.Services;

namespace Car_Rental_System_BE.Application.Services;

public sealed partial class CustomerService(ICustomerRepository repository) : ICustomerService
{
    public Task<CustomerPage> ListAsync(CustomerListQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Page < 1) throw new ArgumentException("Page must be at least 1.", nameof(query));
        if (query.PageSize is < 1 or > 100)
            throw new ArgumentException("Page size must be between 1 and 100.", nameof(query));
        if (query.Search?.Length > 255)
            throw new ArgumentException("Search must contain at most 255 characters.", nameof(query));
        return repository.ListAsync(new CustomerListQuery
        {
            Search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(),
            IsActive = query.IsActive,
            Page = query.Page,
            PageSize = query.PageSize
        }, ct);
    }
}
