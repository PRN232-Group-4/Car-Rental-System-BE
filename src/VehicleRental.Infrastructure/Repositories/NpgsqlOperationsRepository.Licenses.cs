using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Common.Errors;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using Car_Rental_System_BE.Infrastructure.Persistence;

namespace Car_Rental_System_BE.Infrastructure.Repositories;

public sealed partial class NpgsqlOperationsRepository
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

    private static CustomerLicense ReadLicense(NpgsqlDataReader reader) =>
        new(S(reader,0),S(reader,1),D(reader,2),D(reader,3),S(reader,4),reader.GetString(5)) { Id = reader.IsDBNull(6) ? null : reader.GetInt32(6) };

    public async Task<CustomerLicense> SaveLicenseAsync(string email, CustomerLicense x, CancellationToken ct)
    {
        await using var cn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        var customer = await ScalarAsync(cn,tx,"SELECT c.id FROM customers c JOIN users u ON u.id=c.user_id WHERE u.email=$1 FOR UPDATE OF c",email,ct);
        if (customer is not int customerId) throw new KeyNotFoundException();
        // Preserve an older legacy license before switching the compatibility projection.
        await using (var legacy = new NpgsqlCommand("INSERT INTO customer_licenses(customer_id,number,type,issue_date,expiry_date,image_url,status) SELECT customer_id,number,type,issue_date,expiry_date,image_url,status FROM customer_license_records WHERE customer_id=$1 AND id IS NULL ON CONFLICT DO NOTHING",cn,tx))
        { Add(legacy,customerId); await legacy.ExecuteNonQueryAsync(ct); }
        var sql = x.Id is null
            ? "INSERT INTO customer_licenses(customer_id,number,type,issue_date,expiry_date,image_url,status) VALUES($1,$2,$3,$4,$5,$6,'pending') ON CONFLICT(customer_id,number) DO UPDATE SET type=EXCLUDED.type,issue_date=EXCLUDED.issue_date,expiry_date=EXCLUDED.expiry_date,image_url=EXCLUDED.image_url,status='pending' RETURNING number,type,issue_date,expiry_date,image_url,status,id"
            : "UPDATE customer_licenses SET number=$2,type=$3,issue_date=$4,expiry_date=$5,image_url=$6,status='pending' WHERE customer_id=$1 AND id=$7 RETURNING number,type,issue_date,expiry_date,image_url,status,id";
        CustomerLicense result;
        try
        {
            await using var cmd = new NpgsqlCommand(sql,cn,tx);
            Add(cmd,customerId,x.Number!.Trim(),x.Type!.Trim().ToUpperInvariant(),x.IssueDate,x.ExpiryDate,x.ImageUrl);
            if (x.Id is not null) Add(cmd,x.Id.Value);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) throw new KeyNotFoundException("License was not found for this customer.");
            result = ReadLicense(reader);
        }
        catch (PostgresException error) when (error.SqlState == "23505")
        { throw new ConflictException("LICENSE_NUMBER_EXISTS", "Số GPLX này đã tồn tại. Vui lòng chọn bằng đã lưu để cập nhật."); }
        await ProjectLicenseAsync(cn,tx,customerId,result,ct);
        await tx.CommitAsync(ct);
        return result with { Licenses = await ReadLicensesAsync(dataSource,customerId,ct) };
    }

    public Task<CustomerLicense> ReviewLicenseAsync(int customerId, string status, CancellationToken ct) => ReviewLicenseAsync(customerId,status,ct,null);

    public async Task<CustomerLicense> ReviewLicenseAsync(int customerId, string status, CancellationToken ct, int? licenseId)
    {
        await using var cn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        if (await ScalarAsync(cn,tx,"SELECT id FROM customers WHERE id=$1 FOR UPDATE",customerId,ct) is not int) throw new KeyNotFoundException();
        await using (var legacy = new NpgsqlCommand("INSERT INTO customer_licenses(customer_id,number,type,issue_date,expiry_date,image_url,status) SELECT customer_id,number,type,issue_date,expiry_date,image_url,status FROM customer_license_records WHERE customer_id=$1 AND id IS NULL ON CONFLICT DO NOTHING",cn,tx))
        { Add(legacy,customerId); await legacy.ExecuteNonQueryAsync(ct); }
        await using var cmd = new NpgsqlCommand("UPDATE customer_licenses SET status=$1 WHERE customer_id=$2 AND (($3::integer IS NOT NULL AND id=$3) OR ($3::integer IS NULL AND number=(SELECT btrim(license_number) FROM customers WHERE id=$2))) RETURNING number,type,issue_date,expiry_date,image_url,status,id",cn,tx);
        Add(cmd,status,customerId,licenseId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new KeyNotFoundException("License was not found for this customer.");
        var result = ReadLicense(reader);
        await reader.CloseAsync();
        // Reviewing another license must not switch the customer's selected edit record.
        await using var projection = new NpgsqlCommand("UPDATE customers SET license_status=$1 WHERE id=$2 AND btrim(license_number)=$3",cn,tx);
        Add(projection,status,customerId,result.Number);
        await projection.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
        return result with { Licenses = await ReadLicensesAsync(dataSource,customerId,ct) };
    }

    private static async Task ProjectLicenseAsync(NpgsqlConnection cn,NpgsqlTransaction tx,int customerId,CustomerLicense x,CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("UPDATE customers SET license_number=$1,license_type=$2,license_issue_date=$3,license_expiry_date=$4,license_img=$5,license_status=$6 WHERE id=$7",cn,tx);
        Add(cmd,x.Number,x.Type,x.IssueDate,x.ExpiryDate,x.ImageUrl,x.Status,customerId);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
