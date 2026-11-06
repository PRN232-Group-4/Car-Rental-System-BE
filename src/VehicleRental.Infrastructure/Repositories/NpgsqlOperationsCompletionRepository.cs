using Microsoft.EntityFrameworkCore;
using Car_Rental_System_BE.Infrastructure.Persistence;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Repositories;

public sealed class NpgsqlOperationsCompletionRepository(NpgsqlDataSource dataSource) : IOperationsCompletionRepository
{
    public async Task<OperationsPage<DriverScheduleEntry>> DriverScheduleAsync(int? driverId, DateTime from, DateTime to, OperationsPagination pagination, CancellationToken ct)
    {
        from = DateTime.SpecifyKind(from, DateTimeKind.Unspecified);
        to = DateTime.SpecifyKind(to, DateTimeKind.Unspecified);
        await using var cn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        await using var db = VehicleRentalDbContext.Create(cn, tx);
        var source = from assignment in db.Set<VehicleAssignmentsRow>()
            join vehicle in db.Set<VehiclesRow>() on assignment.VehicleId equals vehicle.Id
            join rental in db.Set<RentalsRow>() on assignment.RentalId equals rental.Id
            join staff in db.Set<StaffsRow>() on assignment.DriverStaffId equals staff.Id
            join user in db.Set<UsersRow>() on staff.UserId equals user.Id
            from hourly in db.Set<HourRentalDetailRow>().Where(h => h.RentalId == rental.Id).DefaultIfEmpty()
            from contract in db.Set<LongtermRentalContractRow>().Where(l => l.RentalId == rental.Id).DefaultIfEmpty()
            let start = hourly.StartTime ?? contract.StartTime
            let end = hourly.EndTime ?? contract.EndTime
            where assignment.Status == "assigned" && rental.Status != "cancelled" && rental.Status != "completed" &&
                rental.Status != "expired" && (driverId == null || assignment.DriverStaffId == driverId) && start < to && end > @from
            select new { assignment.Id, assignment.RentalId, assignment.VehicleId, DriverStaffId = staff.Id,
                DriverName = user.Fullname ?? user.Email, Start = start, End = end, RentalStatus = rental.Status,
                VehicleName = (vehicle.Brand ?? "") + (vehicle.Brand != null && vehicle.Model != null ? " " : "") + (vehicle.Model ?? ""),
                vehicle.LicensePlate, assignment.BufferMinutes };
        var total = await source.CountAsync(ct);
        var offset = ((long)pagination.Page - 1) * pagination.PageSize;
        var rows = offset >= total ? [] : await source.OrderBy(row => row.Start).ThenBy(row => row.Id)
            .Skip((int)offset).Take(pagination.PageSize).ToListAsync(ct);
        var items = rows.Select(row => new DriverScheduleEntry(row.Id, row.RentalId, row.VehicleId,
            row.DriverStaffId, row.DriverName, Utc(row.Start!.Value), Utc(row.End!.Value), row.RentalStatus,
            row.VehicleName, row.LicensePlate, row.BufferMinutes)).ToList();
        await tx.CommitAsync(ct);
        return new(items, pagination.Page, pagination.PageSize, total);
    }
    public async Task<OperationsPage<Maintenance>> MaintenanceAsync(int? vehicleId, string? status, DateTime? from, DateTime? to, OperationsPagination pagination, CancellationToken ct)
    {
        await using var cn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        await using var db = VehicleRentalDbContext.Create(cn, tx);
        from = from is null ? null : DateTime.SpecifyKind(from.Value, DateTimeKind.Unspecified);
        to = to is null ? null : DateTime.SpecifyKind(to.Value, DateTimeKind.Unspecified);
        var source = db.Set<VehicleMaintenanceRow>().Where(row =>
            (vehicleId == null || row.VehicleId == vehicleId) && (status == null || row.Status == status) &&
            (from == null || row.CreatedAt >= from) && (to == null || row.CreatedAt < to));
        var total = await source.CountAsync(ct);
        var offset = ((long)pagination.Page - 1) * pagination.PageSize;
        var rows = offset >= total ? [] : await source.OrderByDescending(row => row.Id).Skip((int)offset).Take(pagination.PageSize)
            .Select(row => new { Row = row,
                StaffEmail = (from staff in db.Set<StaffsRow>() join user in db.Set<UsersRow>() on staff.UserId equals user.Id
                    where staff.Id == row.AssignedStaffId select user.Email).FirstOrDefault(),
                FacilityName = db.Set<FacilitiesRow>().Where(f => f.Id == row.FacilityId).Select(f => f.Name).FirstOrDefault()
            }).ToListAsync(ct);
        var items = rows.Select(row => new Maintenance(row.Row.Id, row.Row.VehicleId, row.Row.FacilityId,
            row.Row.AssignedStaffId, row.Row.Status, row.Row.MaintenanceType, row.Row.ScheduledDate,
            row.Row.IsSafe, row.Row.Description, row.Row.Cost, Utc(row.Row.CreatedAt), row.Row.RentalId,
            row.StaffEmail, row.FacilityName)).ToList();
        await tx.CommitAsync(ct);
        return new(items, pagination.Page, pagination.PageSize, total);
    }

    private static DateTime Utc(DateTime stamp) => DateTime.SpecifyKind(stamp, DateTimeKind.Utc);
}
