using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Inspections;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Repositories;

public sealed class NpgsqlInspectionDirectoryRepository(NpgsqlDataSource dataSource):IInspectionDirectoryRepository
{
    public async Task<OperationsPage<InspectionListItem>> ListAsync(InspectionListQuery query,CancellationToken ct)
    {
        query.Validate();
        const string filter=" WHERE ($1::text IS NULL OR i.inspection_type=$1) AND ($2::int IS NULL OR i.rental_id=$2)";
        await using var connection=await dataSource.OpenConnectionAsync(ct);
        await using var transaction=await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,ct);
        await using var count=new NpgsqlCommand("SELECT COUNT(*) FROM inspections i"+filter,connection,transaction);
        AddFilters(count,query);
        var total=Convert.ToInt32(await count.ExecuteScalarAsync(ct));
        await using var command=new NpgsqlCommand("""
            SELECT i.id,i.rental_id,i.vehicle_id,NULLIF(trim(concat_ws(' ',v.brand,v.model)),''),v.license_plate,
                i.inspection_type,i.is_ok,u.email,i.inspected_at,i.odometer_km,i.fuel_battery_level,i.damage_notes,i.img_urls
            FROM inspections i
            LEFT JOIN vehicles v ON v.id=i.vehicle_id
            LEFT JOIN staffs s ON s.id=i.staff_id
            LEFT JOIN users u ON u.id=s.user_id
            """+filter+" ORDER BY i.inspected_at DESC NULLS LAST,i.id DESC LIMIT $3 OFFSET $4",connection,transaction);
        AddFilters(command,query);
        command.Parameters.AddWithValue(query.PageSize);
        command.Parameters.AddWithValue(((long)query.Page-1)*query.PageSize);
        var rows=new List<InspectionListItem>();
        await using(var reader=await command.ExecuteReaderAsync(ct))
        {
            while(await reader.ReadAsync(ct)) rows.Add(new(reader.GetInt32(0),reader.GetInt32(1),reader.IsDBNull(2)?null:reader.GetInt32(2),
                Text(reader,3),Text(reader,4),reader.GetString(5),reader.IsDBNull(6)?null:reader.GetBoolean(6),Text(reader,7),
                reader.IsDBNull(8)?null:DateTime.SpecifyKind(reader.GetDateTime(8),DateTimeKind.Utc),
                reader.IsDBNull(9)?null:reader.GetDecimal(9),reader.IsDBNull(10)?null:reader.GetDecimal(10),Text(reader,11),Text(reader,12)));
        }
        await transaction.CommitAsync(ct);
        return new(rows,query.Page,query.PageSize,total);
    }
    private static void AddFilters(NpgsqlCommand command,InspectionListQuery query)
    {
        command.Parameters.AddWithValue((object?)query.InspectionType??DBNull.Value);
        command.Parameters.AddWithValue((object?)query.RentalId??DBNull.Value);
    }
    private static string? Text(NpgsqlDataReader reader,int ordinal)=>reader.IsDBNull(ordinal)?null:reader.GetString(ordinal);
}
