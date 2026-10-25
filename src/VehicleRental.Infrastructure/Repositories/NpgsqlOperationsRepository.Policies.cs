using System.Data;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Validators;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using Car_Rental_System_BE.Infrastructure.Persistence;

namespace Car_Rental_System_BE.Infrastructure.Repositories;

public sealed partial class NpgsqlOperationsRepository
{
    public async Task<bool> VehicleTypeExistsAsync(int id, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await db.Set<VehicleTypesRow>().AnyAsync(x => x.Id == id, ct);
    }
    public async Task<bool> HasActivePolicyAsync(int? vehicleTypeId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await db.Set<RentalPoliciesRow>().AnyAsync(x => x.VehicleTypeId == vehicleTypeId && x.IsActive, ct);
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
        await using var db = VehicleRentalDbContext.Create(dataSource);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var query = db.Set<RentalPoliciesRow>().AsNoTracking();
        var totalCount = await query.CountAsync(ct);
        var offset = ((long)page - 1) * pageSize;
        var items = offset > int.MaxValue ? new List<RentalPolicy>() : await query.OrderByDescending(p => p.Id)
            .Skip((int)offset).Take(pageSize).Select(p => new RentalPolicy(p.Id, p.VehicleTypeId,
                p.Name, p.VatRate, p.OvertimeFeeRate, p.DepositPercentage, p.IsActive,
                db.Set<RentalsRow>().LongCount(r => r.PolicyId == p.Id),
                db.Set<VehicleTypesRow>().Where(t => t.Id == p.VehicleTypeId).Select(t => t.Name).FirstOrDefault(),
                p.ManagedBy, p.CreatedAt)).ToListAsync(ct);
        await transaction.CommitAsync(ct);
        return new(items, page, pageSize, totalCount);
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
        await using var db = VehicleRentalDbContext.Create(connection, transaction);
        return await db.Set<RentalPoliciesRow>().AnyAsync(p => p.VehicleTypeId == typeId && p.IsActive &&
            (excludedId == null || p.Id != excludedId), ct);
    }

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
                (SELECT COUNT(*) FROM rentals r WHERE r.policy_id=rental_policies.id),
                (SELECT t.name FROM vehicle_types t WHERE t.id=rental_policies.vehicle_type_id), managed_by, created_at
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
