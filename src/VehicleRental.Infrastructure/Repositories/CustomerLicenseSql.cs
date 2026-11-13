using Car_Rental_System_BE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Car_Rental_System_BE.Infrastructure.Repositories;

/// <summary>Self-drive eligibility: an approved license of an allowed class, valid through max(today, end date).</summary>
internal static class CustomerLicenseEligibility
{
    public static IQueryable<CustomerLicenseRecordsRow> EligibleLicenses(VehicleRentalDbContext db, int vehicleId, DateOnly validThrough) =>
        from cl in db.Set<CustomerLicenseRecordsRow>()
        from v in db.Set<VehiclesRow>()
        where v.Id == vehicleId && cl.Status == "approved" && cl.ExpiryDate >= validThrough
            && (v.AllowedLicenseTypes.Length == 0 || v.AllowedLicenseTypes.Contains(cl.Type.Trim().ToUpper()))
        select cl;

    public static async Task<bool> IsEligibleAsync(VehicleRentalDbContext db, int customerId, int vehicleId, DateTime rentalEnd, CancellationToken ct)
    {
        var through = await ValidThroughAsync(db, DateOnly.FromDateTime(rentalEnd), ct);
        return await EligibleLicenses(db, vehicleId, through).AnyAsync(cl => cl.CustomerId == customerId, ct);
    }

    public static async Task<DateOnly> ValidThroughAsync(VehicleRentalDbContext db, DateOnly endDate, CancellationToken ct)
    {
        var today = await db.DatabaseTodayAsync(ct);
        return endDate > today ? endDate : today;
    }
}
