using Car_Rental_System_BE.Application.DTOs.Customers;

namespace Car_Rental_System_BE.Infrastructure.Repositories;

public sealed partial class NpgsqlCustomerRepository
{
    public async Task<CustomerDetail?> FindByIdAsync(int customerId, CancellationToken ct)
    {
        const string sql = """
            SELECT c.id, u.id, u.email, u.fullname, u.gender, u.phone, u.avatar, u.is_active,
                   c.license_number, c.license_type, c.license_issue_date, c.license_expiry_date,
                   c.license_img, c.license_status, c.loyalty_point
            FROM customers c
            JOIN users u ON u.id = c.user_id
            WHERE c.id = $1
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(customerId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.GetBoolean(7), new(reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetFieldValue<DateOnly>(10),
                reader.IsDBNull(11) ? null : reader.GetFieldValue<DateOnly>(11),
                reader.IsDBNull(12) ? null : reader.GetString(12), reader.GetString(13)),
            new(reader.GetInt32(14)));
    }
}
