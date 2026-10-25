using Car_Rental_System_BE.Application.DTOs.Customers;

namespace Car_Rental_System_BE.Application.Interfaces.Repositories;

public interface ICustomerRepository
{
    Task<CustomerPage> ListAsync(CustomerListQuery query, CancellationToken ct);
    Task<CustomerDetail?> FindByIdAsync(int customerId, CancellationToken ct);
}
