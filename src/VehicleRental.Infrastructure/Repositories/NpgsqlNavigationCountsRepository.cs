using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Repositories;

public sealed class NpgsqlNavigationCountsRepository(NpgsqlDataSource dataSource) : INavigationCountsRepository
{
    public async Task<NavigationCounts> GetAsync(NavigationAccess access, CancellationToken ct)
    {
        // One statement observes all queues at the same PostgreSQL snapshot; EXISTS avoids
        // counting multiple inspection evidence rows or customer/user join duplicates.
        await using var command = dataSource.CreateCommand("""
            SELECT
                (SELECT COUNT(*) FROM rentals WHERE $1 AND status='pending'),
                (SELECT COUNT(*) FROM rentals r WHERE $2 AND r.status IN ('pre_inspection','post_inspection')
                    AND NOT EXISTS (SELECT 1 FROM inspections i WHERE i.rental_id=r.id AND i.vehicle_id=r.vehicle_id
                        AND i.inspection_type=CASE r.status WHEN 'pre_inspection' THEN 'pre_rental' ELSE 'post_rental' END)),
                (SELECT COUNT(*) FROM vehicle_maintenance WHERE $3 AND status IN ('open','scheduled')),
                (SELECT COUNT(*) FROM reports WHERE status='open' AND reporter_staff_id IS NOT NULL
                    AND ($4 OR reporter_staff_id=$5)),
                (SELECT COUNT(*) FROM customers c WHERE $1 AND c.license_status='pending'
                    AND NULLIF(btrim(c.license_number),'') IS NOT NULL
                    AND EXISTS (SELECT 1 FROM users u WHERE u.id=c.user_id))
            """);
        command.Parameters.AddWithValue(access.CustomerService);
        command.Parameters.AddWithValue(access.Inspections);
        command.Parameters.AddWithValue(access.Maintenance);
        command.Parameters.AddWithValue(access.AllReports);
        command.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Integer, (object?)access.ReportStaffId ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return new(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4));
    }
}
