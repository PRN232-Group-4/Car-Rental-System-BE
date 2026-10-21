using Car_Rental_System_BE.Application.DTOs.Operations;

namespace Car_Rental_System_BE.Application.Interfaces.Services;

public interface INavigationCountsService
{
    Task<NavigationCounts> GetAsync(NavigationAccess access, CancellationToken ct);
}
