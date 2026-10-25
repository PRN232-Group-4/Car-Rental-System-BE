using Car_Rental_System_BE.Application.Availability;
using Car_Rental_System_BE.Application.Assignments;
using Car_Rental_System_BE.Domain.Availability;
using Car_Rental_System_BE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Availability;

public sealed class NpgsqlDriverAvailability(NpgsqlDataSource dataSource, IOptions<AssignmentOptions>? options = null) : IDriverAvailability
{
    public async Task<bool> AnyAvailableAsync(TimeRange? window, DateTime nowUtc, CancellationToken ct, int? facilityId = null)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        var throughDate = DateOnly.FromDateTime(window?.To ?? nowUtc);
        var drivers = from s in db.Set<StaffsRow>()
            join u in db.Set<UsersRow>() on s.UserId equals u.Id
            where s.IsDriver && u.IsActive && (facilityId == null || s.FacilityId == facilityId)
                && s.LicenseExpiryDate >= throughDate
            select s;
        if (window is not { } w) return await drivers.AnyAsync(ct);

        var range = NpgsqlVehicleSearchRepository.Range(w.From, w.To.AddMinutes(options?.Value.DriverBufferMinutes ?? 60));
        var fromTime = NpgsqlVehicleSearchRepository.Timestamp(w.From);
        var to = NpgsqlVehicleSearchRepository.Timestamp(w.To.AddMinutes(options?.Value.DriverBufferMinutes ?? 60));
        var busy = from a in db.Set<VehicleAssignmentsRow>()
            join r in db.Set<RentalsRow>() on a.RentalId equals r.Id
            join h0 in db.Set<HourRentalDetailRow>() on r.Id equals h0.RentalId into hours
            from h in hours.DefaultIfEmpty()
            join l0 in db.Set<LongtermRentalContractRow>() on r.Id equals l0.RentalId into contracts
            from l in contracts.DefaultIfEmpty()
            where a.Status == "assigned" && r.Status != "cancelled" && r.Status != "completed" && r.Status != "expired"
                && (a.Period != null ? a.Period!.Value.Overlaps(range)
                    : ((h.StartTime ?? l.StartTime) == null || (h.StartTime ?? l.StartTime) < to)
                      && ((h.EndTime ?? l.EndTime) == null || (h.EndTime ?? l.EndTime)!.Value.AddMinutes(a.BufferMinutes) > fromTime))
            select a.DriverStaffId;
        return await drivers.AnyAsync(s => !busy.Contains(s.Id), ct);
    }
}
