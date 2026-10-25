using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Inspections;
using Car_Rental_System_BE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Repositories;

public sealed class NpgsqlInspectionDirectoryRepository(NpgsqlDataSource dataSource) : IInspectionDirectoryRepository
{
    public async Task<OperationsPage<InspectionListItem>> ListAsync(InspectionListQuery query, CancellationToken ct)
    {
        query.Validate();
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        await using var db = VehicleRentalDbContext.Create(connection, transaction);
        var filtered = db.Set<InspectionsRow>().Where(i =>
            (query.InspectionType == null || i.InspectionType == query.InspectionType) &&
            (query.RentalId == null || i.RentalId == query.RentalId));
        var total = await filtered.CountAsync(ct);
        var offset = ((long)query.Page - 1) * query.PageSize;
        var source = from inspection in filtered
            from vehicle in db.Set<VehiclesRow>().Where(v => v.Id == inspection.VehicleId).DefaultIfEmpty()
            from staff in db.Set<StaffsRow>().Where(s => s.Id == inspection.StaffId).DefaultIfEmpty()
            from user in db.Set<UsersRow>().Where(u => u.Id == staff.UserId).DefaultIfEmpty()
            orderby inspection.InspectedAt == null, inspection.InspectedAt descending, inspection.Id descending
            select new { Inspection = inspection, VehicleName = ((vehicle.Brand ?? "") + " " + (vehicle.Model ?? "")).Trim(),
                vehicle.LicensePlate, StaffEmail = user.Email };
        var rows = offset >= total ? [] : await source.Skip((int)offset).Take(query.PageSize).ToListAsync(ct);
        var items = rows.Select(row => new InspectionListItem(row.Inspection.Id, row.Inspection.RentalId,
            row.Inspection.VehicleId, row.VehicleName.Length == 0 ? null : row.VehicleName, row.LicensePlate,
            row.Inspection.InspectionType, row.Inspection.IsOk, row.StaffEmail,
            row.Inspection.InspectedAt is { } stamp ? DateTime.SpecifyKind(stamp, DateTimeKind.Utc) : null,
            row.Inspection.OdometerKm, row.Inspection.FuelBatteryLevel, row.Inspection.DamageNotes,
            row.Inspection.ImgUrls, row.Inspection.ConfirmationSource, row.Inspection.ConfirmedBy)).ToList();
        await transaction.CommitAsync(ct);
        return new(items, query.Page, query.PageSize, total);
    }
}
