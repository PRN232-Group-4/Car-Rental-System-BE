using System.Data;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Validators;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Repositories;

public sealed partial class NpgsqlOperationsRepository
{
    public async Task<bool> VehicleTypeExistsAsync(int id, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("SELECT EXISTS(SELECT 1 FROM vehicle_types WHERE id=$1)");
        command.Parameters.AddWithValue(id);
        return (bool)(await command.ExecuteScalarAsync(ct))!;
    }

    public async Task<bool> HasActivePolicyAsync(int? vehicleTypeId, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("SELECT EXISTS(SELECT 1 FROM rental_policies WHERE vehicle_type_id IS NOT DISTINCT FROM $1::int AND is_active=true)");
        command.Parameters.AddWithValue((object?)vehicleTypeId ?? DBNull.Value);
        return (bool)(await command.ExecuteScalarAsync(ct))!;
    }

    public async Task<RentalPolicy> CreatePolicyAsync(RentalPolicy policy, CancellationToken ct)
    {
        RentalPolicyValidator.Validate(policy);
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        if (policy.VehicleTypeId is { } typeId)
        {
            await using var reference = new NpgsqlCommand("SELECT id FROM vehicle_types WHERE id=$1 FOR KEY SHARE", connection, transaction);
            reference.Parameters.AddWithValue(typeId);
            if (await reference.ExecuteScalarAsync(ct) is null) throw RentalPolicyValidator.InvalidVehicleType();
        }
        await LockPolicyScopeAsync(connection, transaction, policy.VehicleTypeId, ct);
        if (await HasActivePolicyInScopeAsync(connection, transaction, policy.VehicleTypeId, null, ct))
            throw RentalPolicyValidator.ActivePolicyExists();
        await using var command = new NpgsqlCommand("""
            INSERT INTO rental_policies(vehicle_type_id,name,vat_rate,overtime_fee_rate,deposit_percentage,is_active,managed_by)
            VALUES($1,$2,$3,$4,$5,$6,$7)
            RETURNING id,vehicle_type_id,name,vat_rate,overtime_fee_rate,deposit_percentage,is_active,
                0::bigint, (SELECT t.name FROM vehicle_types t WHERE t.id=rental_policies.vehicle_type_id), managed_by, created_at
            """, connection, transaction);
        Add(command, policy.VehicleTypeId, policy.Name, policy.VatRate, policy.OvertimeFeeRate, policy.DepositPercentage, policy.IsActive, policy.ManagedBy);
        RentalPolicy result;
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("Rental policy could not be created.");
            result = ReadPolicyWithCount(reader);
        }
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task<RentalPolicyPage> ListPoliciesAsync(int page, int pageSize, CancellationToken ct)
    {
        RentalPolicyValidator.ValidatePagination(page, pageSize);
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await using var total = new NpgsqlCommand("SELECT COUNT(*) FROM rental_policies", connection, transaction);
        var totalCount = Convert.ToInt32(await total.ExecuteScalarAsync(ct));
        await using var command = new NpgsqlCommand("""
            SELECT p.id,p.vehicle_type_id,p.name,p.vat_rate,p.overtime_fee_rate,p.deposit_percentage,p.is_active,
                (SELECT COUNT(*) FROM rentals r WHERE r.policy_id=p.id) AS rental_count,
                (SELECT t.name FROM vehicle_types t WHERE t.id=p.vehicle_type_id), p.managed_by, p.created_at
            FROM rental_policies p ORDER BY p.id DESC LIMIT $1 OFFSET $2
            """, connection, transaction);
        command.Parameters.AddWithValue(pageSize);
        command.Parameters.AddWithValue(((long)page - 1) * pageSize);
        var items = new List<RentalPolicy>();
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) items.Add(ReadPolicyWithCount(reader));
        await transaction.CommitAsync(ct);
        return new RentalPolicyPage(items, page, pageSize, totalCount);
    }

    private static RentalPolicy ReadPolicyWithCount(NpgsqlDataReader reader) => ReadPolicy(reader) with { RentalCount = reader.GetInt64(7), VehicleTypeName = S(reader,8), ManagedBy = reader.IsDBNull(9)?null:reader.GetInt32(9), CreatedAt = reader.GetDateTime(10) };

    private static async Task LockPolicyScopeAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, int? typeId, CancellationToken ct)
    {
        // Separate namespace from vehicle/assignment locks; 0 represents the global scope.
        await using var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(720011, $1)", connection, transaction);
        command.Parameters.AddWithValue(typeId ?? 0);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<bool> HasActivePolicyInScopeAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, int? typeId, int? excludedId, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS(SELECT 1 FROM rental_policies
                WHERE vehicle_type_id IS NOT DISTINCT FROM $1::int AND is_active=true
                AND ($2::int IS NULL OR id<>$2::int))
            """, connection, transaction);
        Add(command, typeId, excludedId);
        return (bool)(await command.ExecuteScalarAsync(ct))!;
    }
}
