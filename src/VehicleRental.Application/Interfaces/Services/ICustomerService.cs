using Car_Rental_System_BE.Application.DTOs.Customers;

namespace Car_Rental_System_BE.Application.Interfaces.Services;

public interface ICustomerService
{
    Task<CustomerPage> ListAsync(CustomerListQuery query, CancellationToken ct);
    Task<CustomerDetail?> FindByIdAsync(int customerId, CancellationToken ct);
}
