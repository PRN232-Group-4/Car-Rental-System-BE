using System.Data;
using Car_Rental_System_BE.Application.Features.Navigation;
using Car_Rental_System_BE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Features.Navigation;

public sealed class NpgsqlNavigationCountsRepository(NpgsqlDataSource dataSource) : INavigationCountsRepository
{
    public async Task<NavigationCounts> GetAsync(NavigationAccess access, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        // Mọi số đếm giữ cùng một snapshot dù EF gửi các truy vấn tổng hợp riêng lẻ.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var pending = access.CustomerService
            ? await db.Set<RentalsRow>().LongCountAsync(r => r.Status == "pending", ct) : 0;
        var inspections = access.Inspections
            ? await db.Set<RentalsRow>().LongCountAsync(r => (r.Status == "pre_inspection" || r.Status == "post_inspection")
                && !db.Set<InspectionsRow>().Any(i => i.RentalId == r.Id && i.VehicleId == r.VehicleId
                    && i.InspectionType == (r.Status == "pre_inspection" ? "pre_rental" : "post_rental")), ct) : 0;
        var maintenance = access.Maintenance
            ? await db.Set<VehicleMaintenanceRow>().LongCountAsync(m => m.Status == "open" || m.Status == "scheduled", ct) : 0;
        var reports = await db.Set<ReportsRow>().LongCountAsync(r => r.Status == "open" && r.ReporterStaffId != null
            && (access.AllReports || r.ReporterStaffId == access.ReportStaffId), ct);
        var licenses = access.CustomerService
            ? await db.Set<CustomersRow>().LongCountAsync(c => db.Set<CustomerLicenseRecordsRow>()
                .Any(l => l.CustomerId == c.Id && l.Status == "pending") && db.Set<UsersRow>().Any(u => u.Id == c.UserId), ct) : 0;
        await transaction.CommitAsync(ct);
        return new(pending, inspections, maintenance, reports, licenses);
    }
}
