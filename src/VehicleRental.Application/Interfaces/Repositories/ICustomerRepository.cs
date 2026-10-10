using Car_Rental_System_BE.Application.DTOs.Customers;

namespace Car_Rental_System_BE.Application.Interfaces.Repositories;

public partial interface ICustomerRepository
{
    Task<CustomerPage> ListAsync(CustomerListQuery query, CancellationToken ct);
}
