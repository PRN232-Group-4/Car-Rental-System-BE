using Car_Rental_System_BE.Application.DTOs.Customers;

namespace Car_Rental_System_BE.Application.Services;

public sealed partial class CustomerService
{
    public Task<CustomerDetail?> FindByIdAsync(int customerId, CancellationToken ct)
    {
        if (customerId <= 0) throw new ArgumentOutOfRangeException(nameof(customerId), "Customer ID must be positive.");
        return repository.FindByIdAsync(customerId, ct);
    }
}
