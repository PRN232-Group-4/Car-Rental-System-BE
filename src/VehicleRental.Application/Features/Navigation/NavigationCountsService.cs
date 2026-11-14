
namespace Car_Rental_System_BE.Application.Features.Navigation;

public sealed class NavigationCountsService(INavigationCountsRepository repository) : INavigationCountsService
{
    public async Task<NavigationCounts> GetAsync(NavigationAccess access, CancellationToken ct)
    {
        var reports = access.AllReports || access.ReportStaffId > 0;
        if (!access.CustomerService && !access.Maintenance && !access.Inspections && !reports) return NavigationCounts.Empty;
        var counts = await repository.GetAsync(access, ct);
        return new(access.CustomerService ? counts.Rentals : 0,
            access.Inspections ? counts.Inspections : 0,
            access.Maintenance ? counts.Maintenance : 0,
            reports ? counts.Reports : 0,
            access.CustomerService ? counts.Licenses : 0);
    }
}
