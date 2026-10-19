using Car_Rental_System_BE.Application.DTOs.Customers;

namespace Car_Rental_System_BE.Application.Interfaces.Services;

public partial interface ICustomerService
{
    Task<CustomerDetail?> FindByIdAsync(int customerId, CancellationToken ct);
}
