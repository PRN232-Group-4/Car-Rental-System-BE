using Car_Rental_System_BE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.Features.Rentals;
using Car_Rental_System_BE.Domain.Features.Availability;
using Npgsql;
using Car_Rental_System_BE.Infrastructure.Repositories;
using Car_Rental_System_BE.Domain.Features.Rentals;

namespace Car_Rental_System_BE.Infrastructure.Features.Rentals;

public sealed class NpgsqlRentalLifecycleRepository(NpgsqlDataSource dataSource) : IRentalLifecycleRepository
{
    /// <summary>
    /// Đọc thường một đơn thuê - KHÔNG lấy khóa nào ở đây (Phase 2 / D-3: tên cũ GetAsync gây hiểu nhầm).
    /// Bên gọi cần thay đổi dữ liệu phải đi qua hàm có khóa bên trong transaction của chính nó.
    /// </summary>
    public async Task<RentalLifecycleEntity?> GetAsync(int rentalId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        var row = await (from r in db.Set<RentalsRow>()
                         join hourly in db.Set<HourRentalDetailRow>() on r.Id equals hourly.RentalId into hours
                         from h in hours.DefaultIfEmpty()
                         join contract in db.Set<LongtermRentalContractRow>() on r.Id equals contract.RentalId into contracts
                         from l in contracts.DefaultIfEmpty()
                         join policy in db.Set<RentalPoliciesRow>() on r.PolicyId equals policy.Id into policies
                         from p in policies.DefaultIfEmpty()
                         where r.Id == rentalId
                         select new { Rental = r, Start = h.StartTime ?? l.StartTime, End = h.EndTime ?? l.EndTime,
                             HasDriver = (bool?)h.HasDriver ?? (bool?)l.HasDriver ?? false, Hourly = h.HourlyPriceSnapshot ?? 0,
                             Monthly = l.MonthlyPriceSnapshot ?? 0, OvertimeRate = (decimal?)p.OvertimeFeeRate ?? 0, VatRate = (decimal?)p.VatRate ?? 10,
                             HasContract = db.Set<RentalContractDocumentsRow>().Any(d => d.RentalId == r.Id) }).SingleOrDefaultAsync(ct);
        if (row is null) return null;
        var rental = row.Rental;
        return new RentalLifecycleEntity(rental.Id, rental.CustomerId, rental.VehicleId, rental.PolicyId,
            rental.CustomerSubscriptionId, rental.IsSubscription, rental.RentalType, RentalStatusExtensions.FromDbString(rental.Status),
            rental.VatPrice, rental.DepositPrice, rental.OvertimePrice, rental.TotalPrice, row.Start!.Value, row.End!.Value,
            row.HasDriver, row.Hourly, row.Monthly, row.OvertimeRate, row.VatRate, rental.ConfirmedAt, rental.StartedAt,
            rental.ReturnedAt, rental.CompletedAt, rental.CancelledAt, rental.CancelledBy, rental.CancelReason,
            rental.OvertimeHours, row.HasContract, rental.ExpireAt, rental.ExpiredAt);
    }

    public async Task<bool> HasPassingPreInspectionAsync(int rentalId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await (from i in db.Set<InspectionsRow>() join r in db.Set<RentalsRow>() on new { i.RentalId, i.VehicleId } equals new { RentalId = r.Id, r.VehicleId }
                      where i.RentalId == rentalId && i.InspectionType == "pre_rental" && i.IsOk == true select i.Id).AnyAsync(ct);
    }

    public async Task<bool> HasPostInspectionAsync(int rentalId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await (from i in db.Set<InspectionsRow>() join r in db.Set<RentalsRow>() on new { i.RentalId, i.VehicleId } equals new { RentalId = r.Id, r.VehicleId }
                      where i.RentalId == rentalId && i.InspectionType == "post_rental" select i.Id).AnyAsync(ct);
    }

    public async Task<bool> IsPostInspectionOkAsync(int rentalId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await (from i in db.Set<InspectionsRow>() join r in db.Set<RentalsRow>() on new { i.RentalId, i.VehicleId } equals new { RentalId = r.Id, r.VehicleId }
                      where i.RentalId == rentalId && i.InspectionType == "post_rental" orderby i.Id descending select i.IsOk).FirstOrDefaultAsync(ct) ?? false;
    }

    public async Task<bool> HasValidAssignmentAsync(int rentalId, bool hasDriver, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await (from a in db.Set<VehicleAssignmentsRow>() join r in db.Set<RentalsRow>() on new { a.RentalId, a.VehicleId } equals new { RentalId = r.Id, r.VehicleId }
                      where a.RentalId == rentalId && a.Status == "assigned" && (!hasDriver || a.DriverStaffId != null) select a.Id).AnyAsync(ct);
    }

    public async Task<bool> IsCustomerLicenseValidAsync(int customerId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await db.Set<CustomerLicenseRecordsRow>().AnyAsync(l => l.CustomerId == customerId
            && l.Status == "approved" && l.ExpiryDate >= DateOnly.FromDateTime(DateTime.Today), ct);
    }

    /// <summary>Advisory lock theo đơn thuê, sau đó khóa dòng đơn thuê, như trước.</summary>
    private static async Task<RentalsRow?> LockRentalAsync(VehicleRentalDbContext db, int rentalId, CancellationToken ct)
    {
        await db.AdvisoryLockAsync((long)rentalId, ct);
        return await db.LockByKeyAsync<RentalsRow>(rentalId, ct);
    }

    private static Task ReleaseVehicleAsync(VehicleRentalDbContext db, int vehicleId, CancellationToken ct) =>
        db.Set<VehiclesRow>().Where(v => v.Id == vehicleId).ExecuteUpdateAsync(s => s
            .SetProperty(v => v.IsRenting, false)
            .SetProperty(v => v.Status, v => v.Status == "maintenance" ? "maintenance" : "available"), ct);

    public async Task UpdateToPreInspectionAsync(int rentalId, DateTime confirmedAt, ActorContext actor, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var db = VehicleRentalDbContext.Create(conn, tx);

        var rental = await LockRentalAsync(db, rentalId, ct) ?? throw new NotFoundException("RENTAL_NOT_FOUND", "Rental was not found.");
        if (rental.Status == "pre_inspection" && rental.ConfirmedAt is not null)
            throw new ConflictException("ALREADY_IN_STATE", "Rental is already confirmed.");
        if (rental.Status is not ("pending" or "pre_inspection"))
            throw new ConflictException("ILLEGAL_TRANSITION", $"Rental cannot be confirmed from {rental.Status}.");

        await Car_Rental_System_BE.Infrastructure.Repositories.RentalApprovalWriter.WriteAsync(conn, tx, rentalId, rental.Status, confirmedAt, actor, "Rental confirmed", ct);
        await tx.CommitAsync(ct);
    }

    public async Task UpdateToRentingAsync(int rentalId, int vehicleId, DateTime startedAt, ActorContext actor, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var db = VehicleRentalDbContext.Create(conn, tx);

        // Các thao tác điều phối lấy khóa này; phân công mới và đổi xe dùng VehicleLock. Lấy cả hai
        // trước khi khóa dòng đơn thuê và kiểm tra lại bằng chứng bên trong transaction này.
        await db.DispatcherLockAsync(ct);
        await VehicleLock.AcquireAsync(conn, tx, vehicleId, ct);

        var rental = await LockRentalAsync(db, rentalId, ct) ?? throw new NotFoundException("RENTAL_NOT_FOUND", "Rental was not found.");
        if (rental.Status == "renting")
            throw new ConflictException("ALREADY_IN_STATE", "Rental is already started.");
        if (rental.Status != "pre_inspection")
            throw new ConflictException("ILLEGAL_TRANSITION", $"Rental cannot be started from {rental.Status}.");
        if (rental.VehicleId != vehicleId)
            throw new ConflictException("RENTAL_CHANGED", "The assigned vehicle changed. Reload the rental before confirming handover.");

        var started = Sql.Ts(startedAt);
        var schedule = await RequireStartEvidenceAsync(db, conn, tx, rental, vehicleId, started, ct);

        // Bằng chứng, thanh toán và tình trạng trống đã đạt trong khi giữ khóa điều phối/xe/đơn
        // thuê. Ghi nhận bàn giao xe tự lái mà không cần thao tác tài xế riêng.
        if (!schedule.HasDriver && !await db.Set<VehicleAssignmentsRow>().AnyAsync(a => a.RentalId == rentalId && a.Status == "assigned", ct))
        {
            var minimumEnd = started.AddTicks(10); // + interval '1 microsecond'
            await db.InsertAsync(new VehicleAssignmentsRow
            {
                RentalId = rentalId, VehicleId = rental.VehicleId, DriverStaffId = null, Status = "assigned", AssignedBy = actor.Email,
                AssignedAt = started,
                StartsAt = schedule.Start is { } s && s > started ? s : started,
                EndsAt = schedule.End is { } e && e > minimumEnd ? e : minimumEnd,
                Reason = "initial",
            }, ct);
        }

        var now = await db.DatabaseNowAsync(ct);
        await db.Set<RentalsRow>().Where(r => r.Id == rentalId).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.Status, "renting")
            .SetProperty(r => r.StartedAt, started)
            .SetProperty(r => r.StatusChangedAt, now), ct);
        await db.Set<VehiclesRow>().Where(v => v.Id == vehicleId).ExecuteUpdateAsync(s => s
            .SetProperty(v => v.Status, "renting")
            .SetProperty(v => v.IsRenting, true), ct);

        await InsertHistoryAsync(db, rentalId, "pre_inspection", "renting", actor, "Rental started", ct);
        await tx.CommitAsync(ct);
    }

    private sealed record StartSchedule(bool HasDriver, DateTime? Start, DateTime? End);

    private static async Task<StartSchedule> RequireStartEvidenceAsync(VehicleRentalDbContext db, NpgsqlConnection conn, NpgsqlTransaction tx,
        RentalsRow rental, int vehicleId, DateTime startedAt, CancellationToken ct)
    {
        // Một câu lệnh riêng lấy khóa khách hàng trước khi chụp điều kiện đủ.
        if (await db.LockByKeyAsync<CustomersRow>(rental.CustomerId, ct, RowLock.Share) is null)
            throw new ConflictException("CUSTOMER_LICENSE_NOT_VALID", "Rental customer was not found.");
        var rentalId = rental.Id;
        var schedule = await (from r in db.Set<RentalsRow>()
            join h in db.Set<HourRentalDetailRow>() on r.Id equals h.RentalId into hourly
            from h in hourly.DefaultIfEmpty()
            join l in db.Set<LongtermRentalContractRow>() on r.Id equals l.RentalId into longterm
            from l in longterm.DefaultIfEmpty()
            where r.Id == rentalId
            select new StartSchedule((bool?)h.HasDriver ?? (bool?)l.HasDriver ?? false,
                (DateTime?)h.StartTime ?? l.StartTime, (DateTime?)h.EndTime ?? l.EndTime)).SingleAsync(ct);

        if (!await db.Set<InspectionsRow>().AnyAsync(i => i.RentalId == rentalId && i.VehicleId == rental.VehicleId
                && i.InspectionType == "pre_rental" && i.IsOk == true, ct))
            throw new ConflictException("PRE_INSPECTION_REQUIRED", "A passed inspection for the current vehicle is required.");
        var assignmentOk = schedule.HasDriver
            ? await db.Set<VehicleAssignmentsRow>().AnyAsync(a => a.RentalId == rentalId && a.VehicleId == rental.VehicleId
                && a.Status == "assigned" && a.DriverStaffId != null, ct)
            : !await db.Set<VehicleAssignmentsRow>().AnyAsync(a => a.RentalId == rentalId && a.Status == "assigned"
                && (a.VehicleId != rental.VehicleId || a.DriverStaffId != null), ct);
        if (!assignmentOk) throw new ConflictException("ASSIGNMENT_REQUIRED", "An active assignment for the current vehicle is required.");
        if (!schedule.HasDriver)
        {
            var endDate = schedule.End is { } end ? DateOnly.FromDateTime(end) : DateOnly.MinValue;
            var through = await CustomerLicenseEligibility.ValidThroughAsync(db, endDate, ct);
            if (!await CustomerLicenseEligibility.EligibleLicenses(db, rental.VehicleId, through).AnyAsync(cl => cl.CustomerId == rental.CustomerId, ct))
                throw new ConflictException("CUSTOMER_LICENSE_NOT_VALID", "Customer license must be approved and valid for self-drive.");
        }
        if (!rental.IsSubscription && !await db.Set<InvoicesRow>().AnyAsync(i => i.RentalId == rentalId && i.Kind == "rental" && i.Status == "paid", ct))
            throw new ConflictException("PAYMENT_REQUIRED", "Direct rental invoice must be paid before handover.");
        if (rental.RentalType == "longterm" && !await db.Set<RentalContractDocumentsRow>().AnyAsync(d => d.RentalId == rentalId, ct))
            throw new ConflictException("SIGNED_CONTRACT_REQUIRED", "Upload the signed long-term contract before handover.");
        var minimumEnd = startedAt.AddTicks(10);
        var window = new TimeRange(
            schedule.Start is { } s && s < startedAt ? s : startedAt,
            schedule.End is { } e && e > minimumEnd ? e : minimumEnd);
        var availability = await VehicleAvailabilityChecks.EvaluateAsync(conn, tx, vehicleId, window, rentalId, 60, ct);
        VehicleAvailabilityChecks.ThrowIfBlocked(availability, vehicleId);
        return schedule;
    }

    public async Task UpdateToPostInspectionAsync(int rentalId, DateTime returnedAt, ActorContext actor, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var db = VehicleRentalDbContext.Create(conn, tx);

        var curStatus = (await LockRentalAsync(db, rentalId, ct))?.Status;
        if (curStatus == "post_inspection")
            throw new ConflictException("ALREADY_IN_STATE", "Rental vehicle is already returned.");
        if (curStatus != "renting")
            throw new ConflictException("ILLEGAL_TRANSITION", $"Rental cannot be returned from {curStatus}.");

        var returned = Sql.Ts(returnedAt);
        var now = await db.DatabaseNowAsync(ct);
        await db.Set<RentalsRow>().Where(r => r.Id == rentalId).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.Status, "post_inspection")
            .SetProperty(r => r.ReturnedAt, returned)
            .SetProperty(r => r.StatusChangedAt, now), ct);

        await InsertHistoryAsync(db, rentalId, "renting", "post_inspection", actor, "Vehicle returned", ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// Phase 2 / D-1: toàn bộ việc hoàn tất một đơn thuê trong MỘT transaction và theo thứ tự khóa cố định:
    /// <list type="number">
    /// <item>advisory lock theo id đơn thuê (cùng key mà mọi thao tác ghi vòng đời khác dùng);</item>
    /// <item><c>rentals ... FOR UPDATE</c> - khóa đơn thuê TRƯỚC, nên hai lần quyết toán đồng thời của cùng
    /// một đơn sẽ tuần tự tại đây và bên thua thấy trạng thái của bên thắng ở bước dưới;</item> <item>kiểm
    /// tra lại trạng thái: đơn thuê vẫn phải là <c>post_inspection</c>;</item>
    /// <item><c>customer_subscriptions ... FOR UPDATE</c> - khóa gói thuê bao SAU, và chỉ cho đơn thuê theo
    /// gói, là trường hợp duy nhất đọc số giờ còn lại để ghi.</item>
    /// </list>
    /// Sau đó bước thay đổi dữ liệu chạy hoàn tiền, hóa đơn, giờ gói và điểm thành viên qua các cổng trên cùng
    /// kết nối và transaction này (xem <see cref="AmbientTransactionScope"/>): lỗi ở bất kỳ đâu sẽ hoàn tác
    /// toàn bộ việc hoàn tất, kể cả tiền đã ghi.
    /// </summary>
    public async Task<RentalSettlementDto> SettleCompletionAsync(
        int rentalId, SettlementPlan plan, SettlementParticipants participants, ActorContext actor, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var db = VehicleRentalDbContext.Create(conn, tx);
        var rental = await LockRentalAsync(db, rentalId, ct)
            ?? throw new NotFoundException("RENTAL_NOT_FOUND", $"Rental {rentalId} was not found.");
        if (rental.Status == "completed")
            throw new ConflictException("ALREADY_IN_STATE", "Rental is already in completed status.");
        if (rental.Status != "post_inspection")
            throw new ConflictException("ILLEGAL_TRANSITION", $"Rental cannot be completed from {rental.Status}.");

        decimal? remainingPackageHours = null;
        if (rental.IsSubscription && rental.CustomerSubscriptionId is int subscriptionId)
            remainingPackageHours = (await db.LockByKeyAsync<CustomerSubscriptionsRow>(subscriptionId, ct))?.RemainingHours;

        int? shortfallInvoiceId = null;
        int? refundPaymentId = null;
        using (AmbientTransactionScope.Begin(conn, tx))
        {
            if (!rental.IsSubscription && plan.Shortfall > 0)
                shortfallInvoiceId = await participants.InvoiceGateway.CreateOvertimeInvoiceAsync(
                    rentalId, plan.Shortfall, plan.OvertimeVat, plan.InvoiceDueAt, ct);
            if (rental.IsSubscription && rental.CustomerSubscriptionId is int subscriptionToCharge && plan.OvertimeHours > 0)
            {
                var allocation = PackageHourAllocator.Allocate(
                    plan.OvertimeHours, remainingPackageHours, plan.UnitHourlyPrice, plan.OvertimeFeeRate, plan.VatRate);
                await db.InsertAsync(new SubscriptionHourLedgerRow
                {
                    RentalId = rentalId, CustomerSubscriptionId = subscriptionToCharge, Hours = allocation.HoursCharged,
                }, ct);
                await participants.SubscriptionUsage.DeductRemainingHoursAsync(subscriptionToCharge, allocation.HoursCharged, ct);
                if (allocation.ExcessHours > 0)
                    shortfallInvoiceId = await participants.InvoiceGateway.CreateSupplementaryInvoiceAsync(
                        rentalId, allocation.ExcessAmount + allocation.ExcessVat, allocation.ExcessVat, plan.InvoiceDueAt, ct);
            }
            if (plan.RefundAmount > 0)
                refundPaymentId = await participants.PaymentGate.CreateRefundPaymentAsync(
                    rentalId, rental.CustomerId, plan.RefundInvoiceId, plan.RefundAmount, rental.IsSubscription, ct);
            if (plan.LoyaltyBasisAmount is { } loyaltyBasis)
                await participants.LoyaltyAward.AwardPointsAsync(rental.CustomerId, rentalId, loyaltyBasis, ct);
            var now = await db.DatabaseNowAsync(ct);
            await db.Set<RentalsRow>().Where(r => r.Id == rentalId).ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, "completed")
                .SetProperty(r => r.CompletedAt, Sql.Ts(plan.CompletedAt))
                .SetProperty(r => r.ReturnedAt, Sql.Ts(plan.ReturnedAt))
                .SetProperty(r => r.OvertimePrice, plan.OvertimeAmount)
                .SetProperty(r => r.VatPrice, plan.NewVatPrice)
                .SetProperty(r => r.TotalPrice, plan.NewTotalPrice)
                .SetProperty(r => r.OvertimeHours, plan.OvertimeHours)
                .SetProperty(r => r.StatusChangedAt, now), ct);
            await ReleaseVehicleAsync(db, rental.VehicleId, ct);
            await db.InsertAsync(new RentalSettlementsRow
            {
                RentalId = rentalId, ScheduledEnd = Sql.Ts(plan.ScheduledEnd), ReturnedAt = Sql.Ts(plan.ReturnedAt),
                OvertimeMinutes = plan.OvertimeMinutes, OvertimeHours = plan.OvertimeHours,
                OvertimeAmount = plan.OvertimeAmount, OvertimeVat = plan.OvertimeVat,
                DepositHeld = plan.DepositHeld, DepositDeducted = plan.DepositDeducted,
                ShortfallInvoiceId = shortfallInvoiceId, RefundPaymentId = refundPaymentId,
                DamageReviewRequired = plan.DamageReviewRequired, CreatedAt = Sql.Ts(plan.CompletedAt),
            }, ct);
            await InsertHistoryAsync(db, rentalId, "post_inspection", "completed", actor, "Rental completed and settled", ct);
        }
        await tx.CommitAsync(ct);
        return new RentalSettlementDto(rentalId, plan.ScheduledEnd, plan.ReturnedAt, plan.OvertimeMinutes,
            plan.OvertimeHours, plan.OvertimeAmount, plan.OvertimeVat, plan.DepositHeld, plan.DepositDeducted,
            shortfallInvoiceId, refundPaymentId, plan.DamageReviewRequired, plan.CompletedAt);
    }

    public async Task UpdateToCancelledAsync(
        int rentalId,
        int vehicleId,
        DateTime cancelledAt,
        string cancelledBy,
        string reason,
        ActorContext actor,
        CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var db = VehicleRentalDbContext.Create(conn, tx);

        var curStatus = (await LockRentalAsync(db, rentalId, ct))?.Status;
        if (curStatus == "cancelled")
            throw new ConflictException("ALREADY_IN_STATE", "Rental is already cancelled.");
        if (curStatus != "pending" && curStatus != "pre_inspection")
            throw new ConflictException("ILLEGAL_TRANSITION", $"Rental cannot be cancelled from {curStatus}.");

        var cancelled = Sql.Ts(cancelledAt);
        var now = await db.DatabaseNowAsync(ct);
        await db.Set<RentalsRow>().Where(r => r.Id == rentalId).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.Status, "cancelled")
            .SetProperty(r => r.CancelledAt, cancelled)
            .SetProperty(r => r.CancelledBy, cancelledBy)
            .SetProperty(r => r.CancelReason, reason)
            .SetProperty(r => r.StatusChangedAt, now), ct);

        // Trả xe về sẵn sàng nếu không đang bảo trì
        await ReleaseVehicleAsync(db, vehicleId, ct);

        await InsertHistoryAsync(db, rentalId, null, "cancelled", actor, reason, ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// FR-042: ứng viên hết hạn = còn ở trạng thái chờ/pre_inspection, đã qua <c>expire_at</c>
    /// và chưa hoàn tất điều kiện “đã duyệt + đã thanh toán” (gói trả trước chỉ cần duyệt).
    /// </summary>
    public async Task<IReadOnlyList<int>> ListExpiringRentalIdsAsync(DateTime now, int limit, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        var nowAt = DateTime.SpecifyKind(now, DateTimeKind.Unspecified);
        return await db.Set<RentalsRow>().Where(r => (r.Status == "pending" || r.Status == "pre_inspection")
            && r.ExpireAt != null && r.ExpireAt <= nowAt && !(r.ConfirmedAt != null && (r.IsSubscription
                || db.Set<InvoicesRow>().Any(i => i.RentalId == r.Id && i.Kind == "rental" && i.Status == "paid"))))
            .OrderBy(r => r.ExpireAt).ThenBy(r => r.Id).Take(limit).Select(r => r.Id).ToListAsync(ct);
    }

    public async Task<RentalStatus?> UpdateToExpiredAsync(
        int rentalId,
        DateTime expiredAt,
        string reason,
        ActorContext actor,
        CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var db = VehicleRentalDbContext.Create(conn, tx);

        var rental = await LockRentalAsync(db, rentalId, ct);
        if (rental is null) return null;
        var currentStatus = rental.Status;
        var invoicePaid = await db.Set<InvoicesRow>().AnyAsync(i => i.RentalId == rentalId && i.Kind == "rental" && i.Status == "paid", ct);

        // Đã rời trạng thái chờ, chưa tới hạn, hoặc đã đủ điều kiện duyệt + thanh toán: bỏ qua.
        // Điều kiện được kiểm tra lại trong transaction nên duyệt/thanh toán sát giờ hết hạn không bị xử lý hai lần.
        if (currentStatus is not ("pending" or "pre_inspection")) return null;
        if (!RentalExpiryRule.IsPastDeadline(expiredAt, rental.ExpireAt)) return null;
        if (RentalExpiryRule.IsFulfilled(rental.ConfirmedAt is not null, rental.IsSubscription, invoicePaid)) return null;

        var expired = Sql.Ts(expiredAt);
        var now = await db.DatabaseNowAsync(ct);
        await db.Set<RentalsRow>().Where(r => r.Id == rentalId).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.Status, "expired")
            .SetProperty(r => r.ExpiredAt, expired)
            .SetProperty(r => r.CancelReason, reason)
            .SetProperty(r => r.StatusChangedAt, now), ct);

        // Hóa đơn chưa thu tiền bị hủy để khách không thanh toán cho đơn đã hết hạn.
        await db.Set<InvoicesRow>().Where(i => i.RentalId == rentalId && i.Kind == "rental" && i.Status == "unpaid")
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, "cancelled"), ct);

        // Lịch xe được giải phóng tự động: trigger trg_rentals_sync_booking đặt vehicle_bookings.is_blocking=false
        // khi status chuyển sang 'expired', nên xe lại được tìm thấy cho khung giờ đó.
        await InsertHistoryAsync(db, rentalId, currentStatus, RentalStatus.Expired.ToDbString(), actor, reason, ct);
        await tx.CommitAsync(ct);
        return RentalStatusExtensions.FromDbString(currentStatus);
    }

    private static Task InsertHistoryAsync(
        VehicleRentalDbContext db,
        int rentalId,
        string? fromStatus,
        string toStatus,
        ActorContext actor,
        string? reason,
        CancellationToken ct) =>
        db.InsertAsync(new RentalStatusHistoryRow
        {
            RentalId = rentalId, FromStatus = fromStatus, ToStatus = toStatus, ActorUserId = actor.UserId,
            ActorEmail = actor.Email, ActorRole = actor.Role, Reason = reason,
        }, ct);
}
