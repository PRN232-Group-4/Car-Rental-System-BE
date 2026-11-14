using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.Features.RentalPolicies;
using Npgsql;
using System.Data;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Validators;
using Microsoft.EntityFrameworkCore;
using Car_Rental_System_BE.Infrastructure.Persistence;

namespace Car_Rental_System_BE.Infrastructure.Features.RentalPolicies;

/// <summary>
/// Các phiên bản chính sách thuê. Các thành viên lấy từ <c>NpgsqlOperationsRepository</c> (phần partial
/// <c>.Policies.cs</c>) với SQL, transaction và advisory lock giữ nguyên; khóa phạm vi giữ không gian tên riêng
/// (720011) để không đụng khóa xe/phân công.
/// </summary>
public sealed class NpgsqlRentalPolicyRepository(NpgsqlDataSource dataSource) : IRentalPolicyRepository
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
        await using var db = VehicleRentalDbContext.Create(connection, transaction);
        if (policy.VehicleTypeId is { } typeId && await db.LockByKeyAsync<VehicleTypesRow>(typeId, ct, RowLock.KeyShare) is null)
            throw RentalPolicyValidator.InvalidVehicleType();
        await LockPolicyScopeAsync(db, policy.VehicleTypeId, ct);
        if (await HasActivePolicyInScopeAsync(db, policy.VehicleTypeId, null, ct))
            throw RentalPolicyValidator.ActivePolicyExists();
        var row = new RentalPoliciesRow
        {
            VehicleTypeId = policy.VehicleTypeId, Name = policy.Name, VatRate = policy.VatRate, OvertimeFeeRate = policy.OvertimeFeeRate,
            DepositPercentage = policy.DepositPercentage, IsActive = policy.IsActive, ManagedBy = policy.ManagedBy,
        };
        await db.InsertAsync(row, ct);
        var result = await PolicyQuery(db, db.Set<RentalPoliciesRow>().Where(p => p.Id == row.Id)).SingleAsync(ct);
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
        var items = offset > int.MaxValue ? new List<RentalPolicy>() : await PolicyQuery(db, query.OrderByDescending(p => p.Id)
            .Skip((int)offset).Take(pageSize)).ToListAsync(ct);
        await transaction.CommitAsync(ct);
        return new(items, page, pageSize, totalCount);
    }

    private static IQueryable<RentalPolicy> PolicyQuery(VehicleRentalDbContext db, IQueryable<RentalPoliciesRow> policies) =>
        policies.Select(p => new RentalPolicy(p.Id, p.VehicleTypeId,
            p.Name, p.VatRate, p.OvertimeFeeRate, p.DepositPercentage, p.IsActive,
            db.Set<RentalsRow>().LongCount(r => r.PolicyId == p.Id),
            db.Set<VehicleTypesRow>().Where(t => t.Id == p.VehicleTypeId).Select(t => t.Name).FirstOrDefault(),
            p.ManagedBy, p.CreatedAt));

    // Không gian tên tách biệt với khóa xe/phân công; 0 đại diện cho phạm vi toàn cục.
    private static Task LockPolicyScopeAsync(VehicleRentalDbContext db, int? typeId, CancellationToken ct) =>
        db.AdvisoryLockAsync(720011, typeId ?? 0, ct);

    private static Task<bool> HasActivePolicyInScopeAsync(VehicleRentalDbContext db, int? typeId, int? excludedId, CancellationToken ct) =>
        db.Set<RentalPoliciesRow>().AnyAsync(p => p.VehicleTypeId == typeId && p.IsActive &&
            (excludedId == null || p.Id != excludedId), ct);

    public async Task<bool> DeletePolicyAsync(int id, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var db = VehicleRentalDbContext.Create(connection, transaction);
        if (await db.LockByKeyAsync<RentalPoliciesRow>(id, ct) is null) return false;
        // Đơn thuê chụp lại chính sách lúc đặt, nên phiên bản đã được tham chiếu phải giữ lại cho hợp đồng.
        if (await db.Set<RentalsRow>().AnyAsync(r => r.PolicyId == id, ct))
            throw new ConflictException("RENTAL_POLICY_IN_USE", "Phiên bản chính sách đã được gắn với đơn thuê nên không thể xóa. Hãy tạm ngưng phiên bản này thay vì xóa.");
        await db.Set<RentalPoliciesRow>().Where(p => p.Id == id).ExecuteDeleteAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    public async Task<RentalPolicy?> SetPolicyStatusAsync(int id, bool isActive, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var db = VehicleRentalDbContext.Create(connection, transaction);
        var current = await db.LockByKeyAsync<RentalPoliciesRow>(id, ct);
        if (current is null) return null;
        await LockPolicyScopeAsync(db, current.VehicleTypeId, ct);
        if (isActive && !current.IsActive && await HasActivePolicyInScopeAsync(db, current.VehicleTypeId, id, ct))
            throw new InvalidOperationException("Phạm vi loại xe này đã có chính sách đang hoạt động. Vui lòng tạm ngưng chính sách cũ trước khi kích hoạt.");
        await db.Set<RentalPoliciesRow>().Where(p => p.Id == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.IsActive, isActive), ct);
        var result = await PolicyQuery(db, db.Set<RentalPoliciesRow>().Where(p => p.Id == id)).FirstOrDefaultAsync(ct);
        await transaction.CommitAsync(ct);
        return result;
    }
}
