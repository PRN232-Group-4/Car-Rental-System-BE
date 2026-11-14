using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Car_Rental_System_BE.Infrastructure.Features.Availability;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using Car_Rental_System_BE.Infrastructure.Persistence;

namespace Car_Rental_System_BE.Infrastructure.Repositories;

public sealed partial class NpgsqlOperationsRepository(NpgsqlDataSource dataSource) : IOperationsRepository
{
    public async Task<IReadOnlyList<DriverScheduleEntry>> ListDriverScheduleAsync(int? driver, DateTime from, DateTime to, CancellationToken ct)
    {
        from = NpgsqlVehicleSearchRepository.Timestamp(from);
        to = NpgsqlVehicleSearchRepository.Timestamp(to);
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await (from a in db.Set<VehicleAssignmentsRow>().AsNoTracking()
            join v in db.Set<VehiclesRow>() on a.VehicleId equals v.Id
            join r in db.Set<RentalsRow>() on a.RentalId equals r.Id
            join st in db.Set<StaffsRow>() on a.DriverStaffId equals st.Id
            join u in db.Set<UsersRow>() on st.UserId equals u.Id
            join h in db.Set<HourRentalDetailRow>() on r.Id equals h.RentalId into hourly
            from h in hourly.DefaultIfEmpty()
            join l in db.Set<LongtermRentalContractRow>() on r.Id equals l.RentalId into longterm
            from l in longterm.DefaultIfEmpty()
            let start = (DateTime?)h.StartTime ?? l.StartTime
            let end = (DateTime?)h.EndTime ?? l.EndTime
            where a.Status == "assigned" && r.Status != "cancelled" && r.Status != "completed" && r.Status != "expired" &&
                (driver == null || a.DriverStaffId == driver) && start < to && end > @from
            orderby start, a.Id
            select new DriverScheduleEntry(a.Id, a.RentalId, a.VehicleId, a.DriverStaffId!.Value,
                u.Fullname ?? u.Email, start!.Value, end!.Value, r.Status,
                v.Brand == null ? (v.Model ?? "") : v.Model == null ? v.Brand : v.Brand + " " + v.Model, v.LicensePlate, a.BufferMinutes)).ToListAsync(ct);
    }
}
