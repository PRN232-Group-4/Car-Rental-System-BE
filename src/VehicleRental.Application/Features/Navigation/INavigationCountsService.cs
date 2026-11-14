namespace Car_Rental_System_BE.Application.Features.Navigation;

public interface INavigationCountsService
{
    Task<NavigationCounts> GetAsync(NavigationAccess access, CancellationToken ct);
}
