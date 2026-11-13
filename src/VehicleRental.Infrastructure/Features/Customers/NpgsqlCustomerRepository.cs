using Car_Rental_System_BE.Application.Features.Customers;
using Car_Rental_System_BE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Features.Customers;

public sealed class NpgsqlCustomerRepository(NpgsqlDataSource dataSource) : ICustomerRepository
{
    public async Task<CustomerPage> ListAsync(CustomerListQuery query, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        var customers = from c in db.Set<CustomersRow>()
                        join u in db.Set<UsersRow>() on c.UserId equals u.Id
                        where (!query.IsActive.HasValue || u.IsActive == query.IsActive)
                           && (query.Search == null || (u.Fullname ?? "").ToLower().Contains(query.Search.ToLower())
                               || u.Email.ToLower().Contains(query.Search.ToLower())
                               || (u.Phone ?? "").ToLower().Contains(query.Search.ToLower()))
                           && (query.LicenseStatus == null
                               || (query.LicenseStatus == "none" && !db.Set<CustomerLicenseRecordsRow>().Any(l => l.CustomerId == c.Id))
                               || db.Set<CustomerLicenseRecordsRow>().Any(l => l.CustomerId == c.Id && l.Status == query.LicenseStatus))
                        select new { Customer = c, User = u };
        var total = await customers.CountAsync(ct);
        var offset = ((long)query.Page - 1) * query.PageSize;
        if (offset >= total) return new([], query.Page, query.PageSize, total);
        var items = await customers.OrderByDescending(x => x.Customer.Id).Skip((int)offset).Take(query.PageSize)
            .Select(x => new CustomerListItem(x.Customer.Id, x.User.Id, x.User.Email, x.User.Fullname,
                x.User.Phone, x.User.Avatar, x.User.IsActive,
                db.Set<CustomerLicenseRecordsRow>().Any(l => l.CustomerId == x.Customer.Id && l.Status == "pending")
                    ? "pending" : x.Customer.LicenseNumber == null || x.Customer.LicenseNumber.Trim() == ""
                        ? "none" : x.Customer.LicenseStatus, x.Customer.LoyaltyPoint))
            .ToListAsync(ct);
        return new(items, query.Page, query.PageSize, total);
    }

    public async Task<CustomerDetail?> FindByIdAsync(int customerId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        var result = await (from c in db.Set<CustomersRow>()
                            join u in db.Set<UsersRow>() on c.UserId equals u.Id
                            where c.Id == customerId
                            select new CustomerDetail(c.Id, u.Id, u.Email, u.Fullname, u.Gender, u.Phone, u.Avatar,
                                u.IsActive, new CustomerLicenseDetail(c.LicenseNumber, c.LicenseType,
                                    c.LicenseIssueDate, c.LicenseExpiryDate, c.LicenseImg, c.LicenseStatus),
                                new CustomerLoyaltyDetail(c.LoyaltyPoint))).SingleOrDefaultAsync(ct);
        if (result is null) return null;
        return result with { License = result.License with
        {
            Licenses = await NpgsqlCustomerLicenseRepository.ReadLicensesAsync(dataSource, customerId, ct)
        } };
    }
}
