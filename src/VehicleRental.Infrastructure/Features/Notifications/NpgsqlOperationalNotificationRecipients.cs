using Car_Rental_System_BE.Application.Features.Notifications;
using Car_Rental_System_BE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Features.Notifications;

public sealed class NpgsqlOperationalNotificationRecipients(NpgsqlDataSource dataSource) : IOperationalNotificationRecipients
{
    public async Task<OperationalEmailRecipient?> CustomerAsync(int customerId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await (from customer in db.Set<CustomersRow>()
            join user in db.Set<UsersRow>() on customer.UserId equals user.Id
            where customer.Id == customerId && user.IsActive
            select new OperationalEmailRecipient(user.Email, user.Fullname ?? "Quý khách")).FirstOrDefaultAsync(ct);
    }

    public async Task<OperationalEmailRecipient?> RentalCustomerAsync(int rentalId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await (from rental in db.Set<RentalsRow>()
            join customer in db.Set<CustomersRow>() on rental.CustomerId equals customer.Id
            join user in db.Set<UsersRow>() on customer.UserId equals user.Id
            where rental.Id == rentalId && user.IsActive
            select new OperationalEmailRecipient(user.Email, user.Fullname ?? "Quý khách")).FirstOrDefaultAsync(ct);
    }

    public async Task<OperationalReportRecipients?> ReportAsync(int reportId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        var row = await (from report in db.Set<ReportsRow>()
            join rental in db.Set<RentalsRow>() on report.RentalId equals rental.Id
            from customer in db.Set<CustomersRow>().Where(c => c.Id == rental.CustomerId).DefaultIfEmpty()
            from user in db.Set<UsersRow>().Where(u => u.Id == customer.UserId && u.IsActive).DefaultIfEmpty()
            from staff in db.Set<StaffsRow>().Where(s => s.Id == report.ReporterStaffId).DefaultIfEmpty()
            from reporter in db.Set<UsersRow>().Where(u => u.Id == staff.UserId && u.IsActive).DefaultIfEmpty()
            where report.Id == reportId
            select new { RentalId = rental.Id, CustomerEmail = user.Email, CustomerName = user.Fullname,
                ReporterEmail = reporter.Email, ReporterName = reporter.Fullname }).FirstOrDefaultAsync(ct);
        if (row is null) return null;
        var recipients = new List<OperationalEmailRecipient>();
        if (row.CustomerEmail is { } email) recipients.Add(new(email, row.CustomerName ?? "Quý khách"));
        if (row.ReporterEmail is { } staffEmail) recipients.Add(new(staffEmail, row.ReporterName ?? "Nhân viên"));
        return new(row.RentalId, recipients.DistinctBy(r => r.Email, StringComparer.OrdinalIgnoreCase).ToArray());
    }
}
