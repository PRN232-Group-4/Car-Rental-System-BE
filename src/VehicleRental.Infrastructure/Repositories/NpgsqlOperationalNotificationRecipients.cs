using Car_Rental_System_BE.Application.Interfaces.External;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Repositories;

public sealed class NpgsqlOperationalNotificationRecipients(NpgsqlDataSource dataSource) : IOperationalNotificationRecipients
{
    public Task<OperationalEmailRecipient?> CustomerAsync(int customerId, CancellationToken ct) => CustomerQueryAsync(
        "SELECT u.email,COALESCE(u.fullname,'Quý khách') FROM customers c JOIN users u ON u.id=c.user_id WHERE c.id=$1 AND u.is_active", customerId, ct);

    public Task<OperationalEmailRecipient?> RentalCustomerAsync(int rentalId, CancellationToken ct) => CustomerQueryAsync(
        "SELECT u.email,COALESCE(u.fullname,'Quý khách') FROM rentals r JOIN customers c ON c.id=r.customer_id JOIN users u ON u.id=c.user_id WHERE r.id=$1 AND u.is_active", rentalId, ct);

    private async Task<OperationalEmailRecipient?> CustomerQueryAsync(string sql, int id, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new(reader.GetString(0), reader.GetString(1)) : null;
    }

    public async Task<OperationalReportRecipients?> ReportAsync(int reportId, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT p.rental_id,u.email,COALESCE(u.fullname,'Quý khách'),su.email,COALESCE(su.fullname,'Nhân viên')
            FROM reports p JOIN rentals r ON r.id=p.rental_id
            LEFT JOIN customers c ON c.id=r.customer_id
            LEFT JOIN users u ON u.id=c.user_id AND u.is_active
            LEFT JOIN staffs s ON s.id=p.reporter_staff_id
            LEFT JOIN users su ON su.id=s.user_id AND su.is_active
            WHERE p.id=$1
            """);
        command.Parameters.AddWithValue(reportId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        var recipients = new List<OperationalEmailRecipient>();
        if (!reader.IsDBNull(1)) recipients.Add(new(reader.GetString(1), reader.GetString(2)));
        if (!reader.IsDBNull(3)) recipients.Add(new(reader.GetString(3), reader.GetString(4)));
        return new(reader.GetInt32(0), recipients.DistinctBy(r => r.Email, StringComparer.OrdinalIgnoreCase).ToArray());
    }
}
