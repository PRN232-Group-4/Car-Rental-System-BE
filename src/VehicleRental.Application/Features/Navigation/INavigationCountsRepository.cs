namespace Car_Rental_System_BE.Application.Features.Navigation;

public interface INavigationCountsRepository
{
    Task<NavigationCounts> GetAsync(NavigationAccess access, CancellationToken ct);
}
