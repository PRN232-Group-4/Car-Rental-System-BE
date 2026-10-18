using Car_Rental_System_BE.Application.DTOs.Customers;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Repositories;

public sealed partial class NpgsqlCustomerRepository(NpgsqlDataSource dataSource) : ICustomerRepository
{
    public async Task<CustomerPage> ListAsync(CustomerListQuery query, CancellationToken ct)
    {
        const string licenseStatus = "CASE WHEN NULLIF(btrim(c.license_number), '') IS NULL THEN 'none' ELSE c.license_status END";
        const string filter = """
            FROM customers c JOIN users u ON u.id = c.user_id
            WHERE ($1::boolean IS NULL OR u.is_active = $1::boolean)
              AND ($2::text IS NULL OR strpos(lower(COALESCE(u.fullname, '')), lower($2)) > 0
                   OR strpos(lower(u.email), lower($2)) > 0
                   OR strpos(lower(COALESCE(u.phone, '')), lower($2)) > 0)
            """ + " AND ($3::text IS NULL OR (" + licenseStatus + ") = $3::text)";
        await using var count = dataSource.CreateCommand("SELECT COUNT(*) " + filter);
        count.Parameters.AddWithValue((object?)query.IsActive ?? DBNull.Value);
        count.Parameters.AddWithValue((object?)query.Search ?? DBNull.Value);
        count.Parameters.AddWithValue((object?)query.LicenseStatus ?? DBNull.Value);
        var total = Convert.ToInt32(await count.ExecuteScalarAsync(ct));

        await using var command = dataSource.CreateCommand("""
            SELECT c.id, c.user_id, u.email, u.fullname, u.phone, u.avatar,
                   u.is_active,
            """ + " " + licenseStatus + ", c.loyalty_point " + filter + " ORDER BY c.id LIMIT $4 OFFSET $5");
        command.Parameters.AddWithValue((object?)query.IsActive ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)query.Search ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)query.LicenseStatus ?? DBNull.Value);
        command.Parameters.AddWithValue(query.PageSize);
        command.Parameters.AddWithValue(((long)query.Page - 1) * query.PageSize);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var items = new List<CustomerListItem>();
        while (await reader.ReadAsync(ct))
        {
            items.Add(new CustomerListItem(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetBoolean(6), reader.GetString(7), reader.GetInt32(8)));
        }
        return new CustomerPage(items, query.Page, query.PageSize, total);
    }
}
