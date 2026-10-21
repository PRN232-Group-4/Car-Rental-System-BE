using Car_Rental_System_BE.Application.DTOs.Operations;

namespace Car_Rental_System_BE.Application.Interfaces.Repositories;

public interface INavigationCountsRepository
{
    Task<NavigationCounts> GetAsync(NavigationAccess access, CancellationToken ct);
}
