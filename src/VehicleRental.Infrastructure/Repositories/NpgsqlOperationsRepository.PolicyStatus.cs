using Car_Rental_System_BE.Application.DTOs.Operations;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Repositories;

public sealed partial class NpgsqlOperationsRepository
{
    public async Task<RentalPolicy?> SetPolicyStatusAsync(int id, bool isActive, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var lookup = new NpgsqlCommand("SELECT vehicle_type_id,is_active FROM rental_policies WHERE id=$1 FOR UPDATE", connection, transaction);
        lookup.Parameters.AddWithValue(id);
        int? typeId;
        bool wasActive;
        await using (var reader = await lookup.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) return null;
            typeId = reader.IsDBNull(0) ? null : reader.GetInt32(0);
            wasActive = reader.GetBoolean(1);
        }
        await LockPolicyScopeAsync(connection, transaction, typeId, ct);
        if (isActive && !wasActive && await HasActivePolicyInScopeAsync(connection, transaction, typeId, id, ct))
            throw new InvalidOperationException("Phạm vi loại xe này đã có chính sách đang hoạt động. Vui lòng tạm ngưng chính sách cũ trước khi kích hoạt.");
        await using var command = new NpgsqlCommand("""
            UPDATE rental_policies SET is_active = $2 WHERE id = $1
            RETURNING id, vehicle_type_id, name, vat_rate, overtime_fee_rate, deposit_percentage, is_active,
                (SELECT COUNT(*) FROM rentals r WHERE r.policy_id=rental_policies.id)
            """, connection, transaction);
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(isActive);
        RentalPolicy? result;
        await using (var reader = await command.ExecuteReaderAsync(ct))
            result = await reader.ReadAsync(ct) ? ReadPolicyWithCount(reader) : null;
        await transaction.CommitAsync(ct);
        return result;
    }
}
