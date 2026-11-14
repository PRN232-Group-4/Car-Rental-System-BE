using Car_Rental_System_BE.Application.Features.Customers;
using Car_Rental_System_BE.Application.Common.Errors;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using Car_Rental_System_BE.Infrastructure.Persistence;

namespace Car_Rental_System_BE.Infrastructure.Features.Customers;

public sealed class NpgsqlCustomerLicenseRepository(NpgsqlDataSource dataSource) : ICustomerLicenseRepository
{
    public async Task<CustomerLicense?> GetLicenseAsync(string email, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        var row = await (from c in db.Set<CustomersRow>().AsNoTracking()
            join u in db.Set<UsersRow>() on c.UserId equals u.Id
            where u.Email == email
            select new { c.Id, c.LicenseNumber, c.LicenseType, c.LicenseIssueDate, c.LicenseExpiryDate, c.LicenseImg, c.LicenseStatus })
            .FirstOrDefaultAsync(ct);
        if (row is null) return null;
        var legacy = new CustomerLicense(row.LicenseNumber, row.LicenseType, row.LicenseIssueDate,
            row.LicenseExpiryDate, row.LicenseImg, row.LicenseStatus ?? "pending");
        var licenses = await ReadLicensesAsync(dataSource, row.Id, ct);
        var selected = licenses.FirstOrDefault(x => x.Number == legacy.Number?.Trim()) ?? legacy;
        return selected with { Licenses = licenses };
    }

    internal static async Task<IReadOnlyList<CustomerLicense>> ReadLicensesAsync(NpgsqlDataSource source, int customerId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(source);
        return await db.Set<CustomerLicenseRecordsRow>().AsNoTracking().Where(x => x.CustomerId == customerId)
            .OrderByDescending(x => x.Id != null).ThenByDescending(x => x.Id).ThenBy(x => x.Number)
            .Select(x => new CustomerLicense(x.Number, x.Type, x.IssueDate, x.ExpiryDate, x.ImageUrl, x.Status) { Id = x.Id })
            .ToListAsync(ct);
    }


    private static CustomerLicense ToLicense(CustomerLicensesRow row) =>
        new(row.Number, row.Type, row.IssueDate, row.ExpiryDate, row.ImageUrl, row.Status) { Id = row.Id };

    /// <summary>Giữ GPLX kiểu cũ (customers.*) thành một dòng customer_licenses; theo ngữ nghĩa ON CONFLICT DO NOTHING.</summary>
    private static async Task PreserveLegacyLicenseAsync(VehicleRentalDbContext db, int customerId, CancellationToken ct)
    {
        var legacy = await db.Set<CustomerLicenseRecordsRow>().AsNoTracking()
            .Where(x => x.CustomerId == customerId && x.Id == null).ToListAsync(ct);
        foreach (var x in legacy)
            await db.TryInsertAsync(new CustomerLicensesRow
            {
                CustomerId = x.CustomerId, Number = x.Number, Type = x.Type, IssueDate = x.IssueDate,
                ExpiryDate = x.ExpiryDate, ImageUrl = x.ImageUrl, Status = x.Status,
            }, ct);
    }

    public async Task<CustomerLicense> SaveLicenseAsync(string email, CustomerLicense x, CancellationToken ct)
    {
        await using var cn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        await using var db = VehicleRentalDbContext.Create(cn, tx);
        var customerId = await (from c in db.Set<CustomersRow>() join u in db.Set<UsersRow>() on c.UserId equals u.Id
            where u.Email == email select (int?)c.Id).FirstOrDefaultAsync(ct);
        if (customerId is null || await db.LockByKeyAsync<CustomersRow>(customerId.Value, ct) is null) throw new KeyNotFoundException();
        // Giữ lại GPLX kiểu cũ trước khi đổi phép chiếu tương thích.
        await PreserveLegacyLicenseAsync(db, customerId.Value, ct);
        var number = x.Number!.Trim();
        var type = x.Type!.Trim().ToUpperInvariant();
        CustomerLicense result;
        try
        {
            // Khóa dòng khách hàng tuần tự hóa việc ghi GPLX, nên tìm-rồi-ghi thay cho ON CONFLICT(customer_id,number).
            var targetId = x.Id ?? await db.Set<CustomerLicensesRow>()
                .Where(l => l.CustomerId == customerId && l.Number == number).Select(l => (int?)l.Id).FirstOrDefaultAsync(ct);
            if (targetId is null)
            {
                var row = new CustomerLicensesRow
                {
                    CustomerId = customerId.Value, Number = number, Type = type, IssueDate = x.IssueDate,
                    ExpiryDate = x.ExpiryDate, ImageUrl = x.ImageUrl, Status = "pending",
                };
                await db.InsertAsync(row, ct);
                targetId = row.Id;
            }
            else if (await db.Set<CustomerLicensesRow>().Where(l => l.CustomerId == customerId && l.Id == targetId).ExecuteUpdateAsync(s => s
                         .SetProperty(l => l.Number, number)
                         .SetProperty(l => l.Type, type)
                         .SetProperty(l => l.IssueDate, x.IssueDate)
                         .SetProperty(l => l.ExpiryDate, x.ExpiryDate)
                         .SetProperty(l => l.ImageUrl, x.ImageUrl)
                         .SetProperty(l => l.Status, "pending"), ct) == 0)
                throw new KeyNotFoundException("License was not found for this customer.");
            result = ToLicense(await db.Set<CustomerLicensesRow>().AsNoTracking().SingleAsync(l => l.Id == targetId, ct));
        }
        catch (Exception error) when (error.SqlState() == "23505")
        { throw new ConflictException("LICENSE_NUMBER_EXISTS", "Số GPLX này đã tồn tại. Vui lòng chọn bằng đã lưu để cập nhật."); }
        await ProjectLicenseAsync(db, customerId.Value, result, ct);
        await tx.CommitAsync(ct);
        return result with { Licenses = await ReadLicensesAsync(dataSource, customerId.Value, ct) };
    }

    public Task<CustomerLicense> ReviewLicenseAsync(int customerId, string status, CancellationToken ct) => ReviewLicenseAsync(customerId,status,ct,null);

    public async Task<CustomerLicense> ReviewLicenseAsync(int customerId, string status, CancellationToken ct, int? licenseId)
    {
        await using var cn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        await using var db = VehicleRentalDbContext.Create(cn, tx);
        var customer = await db.LockByKeyAsync<CustomersRow>(customerId, ct) ?? throw new KeyNotFoundException();
        await PreserveLegacyLicenseAsync(db, customerId, ct);
        var selectedNumber = customer.LicenseNumber?.Trim();
        var target = await db.Set<CustomerLicensesRow>().Where(l => l.CustomerId == customerId &&
                (licenseId != null ? l.Id == licenseId : l.Number == selectedNumber))
            .Select(l => (int?)l.Id).FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("License was not found for this customer.");
        await db.Set<CustomerLicensesRow>().Where(l => l.Id == target).ExecuteUpdateAsync(s => s.SetProperty(l => l.Status, status), ct);
        var result = ToLicense(await db.Set<CustomerLicensesRow>().AsNoTracking().SingleAsync(l => l.Id == target, ct));
        // Duyệt một GPLX khác không được làm đổi bản ghi đang chọn để chỉnh sửa của khách.
        await db.Set<CustomersRow>().Where(c => c.Id == customerId && c.LicenseNumber!.Trim() == result.Number)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LicenseStatus, status), ct);
        await tx.CommitAsync(ct);
        return result with { Licenses = await ReadLicensesAsync(dataSource, customerId, ct) };
    }

    private static Task ProjectLicenseAsync(VehicleRentalDbContext db, int customerId, CustomerLicense x, CancellationToken ct) =>
        db.Set<CustomersRow>().Where(c => c.Id == customerId).ExecuteUpdateAsync(s => s
            .SetProperty(c => c.LicenseNumber, x.Number)
            .SetProperty(c => c.LicenseType, x.Type)
            .SetProperty(c => c.LicenseIssueDate, x.IssueDate)
            .SetProperty(c => c.LicenseExpiryDate, x.ExpiryDate)
            .SetProperty(c => c.LicenseImg, x.ImageUrl)
            .SetProperty(c => c.LicenseStatus, x.Status), ct);
}
