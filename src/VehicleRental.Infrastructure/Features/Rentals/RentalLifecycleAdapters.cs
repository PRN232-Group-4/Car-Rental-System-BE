using Car_Rental_System_BE.Infrastructure.Persistence;
using Car_Rental_System_BE.Infrastructure.Features.Billing;
using Microsoft.EntityFrameworkCore;
using Car_Rental_System_BE.Application.Features.Rentals;
using Car_Rental_System_BE.Application.Interfaces.External;
using Car_Rental_System_BE.Application.Features.Loyalty;
using Microsoft.Extensions.Logging;
using Npgsql;
using Car_Rental_System_BE.Domain.Features.Loyalty;
using Car_Rental_System_BE.Domain.Features.Rentals;
using Car_Rental_System_BE.Domain.Features.Billing;

namespace Car_Rental_System_BE.Infrastructure.Features.Rentals;

public sealed class RentalPaymentGateAdapter(NpgsqlDataSource dataSource) : IRentalPaymentGate
{
    public async Task<bool> IsRentalInvoicePaidAsync(int rentalId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await db.Set<InvoicesRow>().AnyAsync(i => i.RentalId == rentalId && i.Kind == "rental" && i.Status == "paid", ct);
    }

    /// <summary>
    /// Phase 2 / D2: chỉ lấy loại <c>rental</c>. Id này chỉ dùng cho hoàn tiền, và đơn thuê theo gói giờ còn có
    /// thêm hóa đơn <c>supplementary</c>; thứ tự cũ "ưu tiên kind='rental', nếu không thì lấy bất kỳ" sẽ trả hóa
    /// đơn đó về làm hóa đơn chính. Hiện tại trường hợp này không xảy ra (<see cref="GetDepositHeldAsync"/> trả 0
    /// cho đơn thuê theo gói, nên không bao giờ phát sinh hoàn cọc cho chúng), nên đây là bịt một lỗ hổng tiềm ẩn
    /// chứ không phải sửa lỗi đang xảy ra.
    /// </summary>
    public async Task<int?> GetPrimaryInvoiceIdAsync(int rentalId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await db.Set<InvoicesRow>().Where(i => i.RentalId == rentalId && i.Kind == "rental")
            .OrderBy(i => i.Id).Select(i => (int?)i.Id).FirstOrDefaultAsync(ct);
    }

    public async Task<decimal> GetDepositHeldAsync(int rentalId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await db.Set<RentalsRow>().Where(r => r.Id == rentalId).Select(r => r.IsSubscription ? 0m
            : db.Set<InvoicesRow>().Any(i => i.RentalId == rentalId && i.Status == "paid") ? r.DepositPrice : 0m).SingleOrDefaultAsync(ct);
    }

    public async Task<decimal> GetTotalPaidAmountAsync(int rentalId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await db.Set<PaymentsRow>().Where(p => p.RentalId == rentalId && p.Status == "paid" && p.Type != null && p.Type != "refund")
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;
    }

    /// <summary>
    /// Phase 2 / D-5 + D-6: cách DUY NHẤT để ghi nhận một khoản hoàn tiền. Bên trong quyết toán thì nhập vào
    /// transaction dùng chung và không commit gì ở đây (quyết toán sẽ commit); bên ngoài (hủy đơn) thì hàm tự mở
    /// transaction bao quanh đúng lệnh insert có chặn đó. Không có cơ chế chống trùng thứ hai.
    /// </summary>
    public async Task<int> CreateRefundPaymentAsync(
        int rentalId,
        int customerId,
        int? invoiceId,
        decimal amount,
        bool isSubscription,
        CancellationToken ct)
    {
        var ambient = AmbientTransactionScope.Current;
        if (ambient is not null)
        {
            var refundId = await RentalRefundWriter.EnsureRefundAsync(
                ambient.Connection, ambient.Transaction, rentalId, customerId, invoiceId, amount, isSubscription, "deposit", ct);
            return refundId ?? throw new InvalidOperationException($"The refund for rental {rentalId} could not be recorded.");
        }

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await using var db = VehicleRentalDbContext.Create(conn, tx);
        await db.AdvisoryLockAsync((long)rentalId, ct);
        await db.LockByKeyAsync<RentalsRow>(rentalId, ct);

        var standaloneRefundId = await RentalRefundWriter.EnsureRefundAsync(
            conn, tx, rentalId, customerId, invoiceId, amount, isSubscription, "deposit", ct);
        await tx.CommitAsync(ct);
        return standaloneRefundId ?? throw new InvalidOperationException($"The refund for rental {rentalId} could not be recorded.");
    }

    public async Task VoidUnpaidInvoiceAsync(int rentalId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        await db.Set<InvoicesRow>().Where(i => i.RentalId == rentalId && i.Status == "unpaid")
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, "cancelled"), ct);
    }
}

public sealed class InvoiceGatewayAdapter(NpgsqlDataSource dataSource, TimeProvider timeProvider) : IInvoiceGateway
{
    public Task<int> CreateOvertimeInvoiceAsync(
        int rentalId,
        decimal amount,
        decimal vatAmount,
        DateTime dueAt,
        CancellationToken ct)
        => CreateExcessInvoiceAsync(rentalId, amount, vatAmount, dueAt, InvoiceKinds.Overtime, "OT", ct);

    /// <summary>Phase 2 / D2: phần quá giờ vượt gói của đơn thuê theo gói, tính trên hóa đơn riêng.</summary>
    public Task<int> CreateSupplementaryInvoiceAsync(
        int rentalId,
        decimal amount,
        decimal vatAmount,
        DateTime dueAt,
        CancellationToken ct)
        => CreateExcessInvoiceAsync(rentalId, amount, vatAmount, dueAt, InvoiceKinds.Supplementary, "SUP", ct);

    private async Task<int> CreateExcessInvoiceAsync(
        int rentalId, decimal amount, decimal vatAmount, DateTime dueAt, string kind, string numberSuffix, CancellationToken ct)
    {
        // invoices.amount là TỔNG số khách phải trả, đã gồm VAT; invoices.vat_amount là thành phần chi tiết
        // NẰM TRONG tổng đó, giống hệt hóa đơn thuê (CreateInvoiceForRentalAsync ghi amount = total_price +
        // deposit_price, trong đó total_price đã gồm VAT). Các luồng thu tiền (ConfirmCashAsync,
        // CompleteInvoiceAsync, ReserveInvoiceCheckoutAsync) chỉ thu invoices.amount.
        //
        // Vì vậy MỌI bên gọi đều truyền vào đây tổng đã gồm VAT. Hai bên gọi tính ra tổng đó theo cách khác
        // nhau và không được nhầm lẫn: luồng quá giờ của đơn thuê trực tiếp truyền plan.Shortfall, vốn đã gồm
        // VAT, còn luồng gói thuê bao truyền phần vượt chưa có VAT và tự cộng VAT trước. Cộng VAT bên trong
        // hàm này từng khiến luồng thuê trực tiếp bị tính VAT hai lần (đo được: 200.000 + 20.000 VAT bị ghi
        // thành 240.000 trong khi chỉ nợ 220.000).

        // Bên trong quyết toán, hóa đơn phải được ghi trên kết nối và transaction của quyết toán, nếu không
        // một lỗi sau đó sẽ để lại hóa đơn cho một đơn thuê chưa bao giờ hoàn tất.
        var ambient = AmbientTransactionScope.Current;
        await using var db = ambient is null ? VehicleRentalDbContext.Create(dataSource)
            : VehicleRentalDbContext.Create(ambient.Connection, ambient.Transaction);
        var existingId = await db.Set<InvoicesRow>().Where(i => i.RentalId == rentalId && i.Kind == kind)
            .OrderBy(i => i.Id).Select(i => (int?)i.Id).FirstOrDefaultAsync(ct);
        if (existingId is not null) return existingId.Value;

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var invoiceNumber = $"INV-{now:yyyyMMdd}-{rentalId}-{numberSuffix}";

        var row = new InvoicesRow
        {
            RentalId = rentalId, InvoiceNumber = invoiceNumber, Amount = amount, VatAmount = vatAmount,
            Status = "unpaid", DueAt = Sql.Ts(dueAt), Kind = kind,
        };
        await db.InsertAsync(row, ct);
        return row.Id;
    }
}

public sealed class SubscriptionUsageAdapter(NpgsqlDataSource dataSource) : ISubscriptionUsage
{
    public async Task DeductRemainingHoursAsync(int subscriptionId, decimal hours, CancellationToken ct)
    {
        if (subscriptionId <= 0 || hours <= 0) return;

        // Phase 2 / D-2: việc trừ giờ chạy bên trong transaction quyết toán, ngay sau dòng sổ cái theo
        // đơn thuê, dòng này khiến không thể trừ lần hai cho cùng một đơn.
        var ambient = AmbientTransactionScope.Current;
        await using var db = ambient is null ? VehicleRentalDbContext.Create(dataSource)
            : VehicleRentalDbContext.Create(ambient.Connection, ambient.Transaction);
        await db.Set<CustomerSubscriptionsRow>().Where(s => s.Id == subscriptionId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RemainingHours, x => Math.Max(0m, (x.RemainingHours ?? 0m) - hours)), ct);
    }
}

public sealed class LoyaltyAwardAdapter(
    ILoyaltyRepository loyaltyRepo,
    ILogger<LoyaltyAwardAdapter> logger,
    LoyaltyOptions? options = null) : ILoyaltyAward
{
    private readonly LoyaltyOptions _options = options ?? new LoyaltyOptions();

    /// <summary>
    /// Phase 2 / D-5, cố ý đổi hành vi: hàm này không còn nuốt lỗi. Trước đây nó bắt mọi exception và chỉ ghi
    /// log, nên lỗi điểm thành viên âm thầm dẫn tới "hoàn tất mà không có điểm". Giờ lỗi được ném ra, nên lần
    /// quyết toán gọi nó sẽ hoàn tác và đơn thuê KHÔNG được hoàn tất khi thiếu điểm khách đã kiếm được. Bên trong
    /// quyết toán, điểm được ghi trên chính kết nối và transaction của quyết toán, nên cũng không thể còn lại sau
    /// một lần hoàn tác về sau.
    /// </summary>
    public async Task AwardPointsAsync(int customerId, int rentalId, decimal basisAmount, CancellationToken ct)
    {
        if (basisAmount <= 0) return;

        var lifetimeEarned = await loyaltyRepo.GetLifetimeEarnedAsync(customerId, ct);
        var tiers = _options.ToTierConfigs();
        var earnResult = LoyaltyDomainRules.CalculateEarn(basisAmount, lifetimeEarned, _options.VndPerPoint, tiers);
        if (earnResult.EarnedPoints <= 0)
        {
            logger.LogDebug("Rental {RentalId} earned no loyalty points on a basis of {BasisAmount}. Nothing to award.", rentalId, basisAmount);
            return;
        }

        var reason = $"Rental #{rentalId} completed ({earnResult.Multiplier:F2}x {earnResult.TierName} tier)";
        var ambient = AmbientTransactionScope.Current;
        await loyaltyRepo.AwardForRentalAsync(
            customerId, rentalId, earnResult.EarnedPoints, reason,
            ambient?.Connection, ambient?.Transaction, ct);
    }
}

public sealed class RentalEventSink(ILogger<RentalEventSink> logger, IOperationalNotifications? notifications = null) : IRentalEventSink
{
    public async Task PublishStatusChangedAsync(RentalStatusChangedEvent evt, CancellationToken ct)
    {
        logger.LogInformation(
            "Rental status changed: {RentalId} {FromStatus} -> {ToStatus} by {ActorRole} at {OccurredAt:O}",
            evt.RentalId, evt.FromStatus, evt.ToStatus, evt.Actor.Role, evt.OccurredAt);
        if (notifications is not null)
            await notifications.RentalStatusChangedAsync(evt.RentalId, evt.ToStatus.ToDbString());
    }
}

public sealed class CurrentCustomerService(NpgsqlDataSource dataSource) : ICurrentCustomer
{
    public async Task<int?> GetCustomerIdByEmailAsync(string email, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;

        await using var db = VehicleRentalDbContext.Create(dataSource);
        var normalized = email.Trim().ToLowerInvariant();
        return await (from c in db.Set<CustomersRow>() join u in db.Set<UsersRow>() on c.UserId equals u.Id
                      where u.Email.ToLower() == normalized && u.IsActive select (int?)c.Id).FirstOrDefaultAsync(ct);
    }
}
