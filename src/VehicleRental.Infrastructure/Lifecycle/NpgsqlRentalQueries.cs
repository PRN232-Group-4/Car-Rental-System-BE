using Car_Rental_System_BE.Application.Lifecycle;
using Car_Rental_System_BE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using VehicleRental.Domain.Lifecycle;

namespace Car_Rental_System_BE.Infrastructure.Lifecycle;

public sealed class NpgsqlRentalQueries(NpgsqlDataSource dataSource, TimeProvider timeProvider) : IRentalQueryRepository
{
    private sealed class RentalWindow
    {
        public RentalsRow Rental { get; init; } = null!;
        public DateTime? Start { get; init; }
        public DateTime? End { get; init; }
        public bool HasDriver { get; init; }
        public decimal HourlyPrice { get; init; }
        public decimal MonthlyPrice { get; init; }
    }

    private static IQueryable<RentalWindow> Windows(VehicleRentalDbContext db) =>
        from r in db.Set<RentalsRow>()
        join h0 in db.Set<HourRentalDetailRow>() on r.Id equals h0.RentalId into hours
        from h in hours.DefaultIfEmpty()
        join l0 in db.Set<LongtermRentalContractRow>() on r.Id equals l0.RentalId into contracts
        from l in contracts.DefaultIfEmpty()
        select new RentalWindow
        {
            Rental = r, Start = h.StartTime ?? l.StartTime, End = h.EndTime ?? l.EndTime,
            HasDriver = (bool?)h.HasDriver ?? (bool?)l.HasDriver ?? false,
            HourlyPrice = h.HourlyPriceSnapshot ?? 0m, MonthlyPrice = l.MonthlyPriceSnapshot ?? 0m
        };

    public async Task<PagedResult<CustomerRentalSummary>> ListMyRentalsAsync(int customerId, string? tab, int page, int pageSize, CancellationToken ct)
    {
        RentalQueryValidation.Pagination(page, pageSize);
        RentalQueryValidation.CustomerTab(tab);
        await using var db = VehicleRentalDbContext.Create(dataSource);
        var rentals = Windows(db).Where(x => x.Rental.CustomerId == customerId);
        rentals = tab?.Trim().ToLowerInvariant() switch
        {
            "upcoming" => rentals.Where(x => x.Rental.Status == "pending" || x.Rental.Status == "pre_inspection"),
            "ongoing" => rentals.Where(x => x.Rental.Status == "renting"),
            "completed" => rentals.Where(x => x.Rental.Status == "post_inspection" || x.Rental.Status == "completed"),
            "cancelled" => rentals.Where(x => x.Rental.Status == "cancelled"),
            _ => rentals
        };
        var total = await rentals.CountAsync(ct);
        var offset = ((long)page - 1) * pageSize;
        if (offset >= total) return new([], total, page, pageSize);
        var rows = await (from x in rentals
            join v in db.Set<VehiclesRow>() on x.Rental.VehicleId equals v.Id
            join t in db.Set<VehicleTypesRow>() on v.VehicleTypeId equals t.Id
            join f0 in db.Set<FacilitiesRow>() on v.FacilityId equals f0.Id into facilities
            from f in facilities.DefaultIfEmpty()
            join i0 in db.Set<InvoicesRow>().Where(i => i.Kind == "rental") on x.Rental.Id equals i0.RentalId into invoices
            from i in invoices.DefaultIfEmpty()
            orderby x.Rental.Id descending
            select new { Window = x, Vehicle = v, Category = t.Type, Facility = f, Invoice = i,
                OrderId = db.Set<RentalOrdersRow>().Where(o => o.RentalId == x.Rental.Id).Select(o => (int?)o.Id).FirstOrDefault() })
            .Skip((int)offset).Take(pageSize).ToListAsync(ct);
        var items = rows.Select(x => new CustomerRentalSummary(x.Window.Rental.Id, x.Window.Rental.Status,
            x.Window.Rental.RentalType, x.Window.Rental.IsSubscription, x.Vehicle.Id, x.Vehicle.Brand ?? "Unknown",
            x.Vehicle.Model ?? "Unknown", x.Vehicle.MainImgUrl, x.Window.Start!.Value, x.Window.End!.Value,
            x.Window.Rental.TotalPrice, x.Window.Rental.CreatedAt, x.OrderId, x.Vehicle.LicensePlate, x.Category,
            x.Window.Rental.Status is "pending" or "pre_inspection",
            NextAction(x.Window.Rental.Status, x.Window.Rental.ConfirmedAt != null, x.Invoice?.Status, x.Window.Rental.IsSubscription),
            x.Facility == null ? null : new CustomerFacilityDto(x.Facility.Id, x.Facility.Name, x.Facility.Phone),
            SubscriptionHours(x.Window), x.Window.Rental.ExpireAt)).ToList();
        return new(items, total, page, pageSize);
    }

    public async Task<CustomerRentalDetail?> GetMyRentalDetailAsync(int rentalId, int customerId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        var row = await (from x in Windows(db)
            join v in db.Set<VehiclesRow>() on x.Rental.VehicleId equals v.Id
            join f0 in db.Set<FacilitiesRow>() on v.FacilityId equals f0.Id into facilities
            from f in facilities.DefaultIfEmpty()
            join i0 in db.Set<InvoicesRow>().Where(i => i.Kind == "rental") on x.Rental.Id equals i0.RentalId into invoices
            from i in invoices.DefaultIfEmpty()
            where x.Rental.Id == rentalId && x.Rental.CustomerId == customerId
            select new { Window = x, Vehicle = v, Facility = f, Invoice = i,
                OrderId = db.Set<RentalOrdersRow>().Where(o => o.RentalId == rentalId).Select(o => (int?)o.Id).FirstOrDefault() }).FirstOrDefaultAsync(ct);
        if (row == null) return null;
        var r = row.Window.Rental;
        var price = new CustomerPriceDto(r.TotalPrice - r.VatPrice, r.VatPrice, r.DepositPrice, r.OvertimePrice, r.TotalPrice);
        var invoice = Invoice(row.Invoice);
        CustomerDriverDto? driver = null;
        if (row.Window.HasDriver)
            driver = await (from a in db.Set<VehicleAssignmentsRow>()
                join s in db.Set<StaffsRow>() on a.DriverStaffId equals s.Id
                join u in db.Set<UsersRow>() on s.UserId equals u.Id
                where a.RentalId == rentalId && a.Status == "assigned" && a.DriverStaffId != null
                select new CustomerDriverDto(u.Fullname!, u.Avatar, s.Rating)).FirstOrDefaultAsync(ct);
        var inspections = await Inspections(db, rentalId, ct);
        var settlement = await Settlement(db, rentalId, ct);
        var history = await History(db, rentalId, ct);
        var timeline = history.Select(h => new CustomerTimelineDto(h.ToStatus, h.CreatedAt,
            (h.ActorRole ?? "system").ToLowerInvariant() switch { "customer" => "customer", "system" => "system", _ => "staff" }, h.Reason)).ToList();
        var canCancel = r.Status is "pending" or "pre_inspection";
        var cancellation = canCancel ? CancellationPolicy.Evaluate(timeProvider.GetUtcNow().UtcDateTime, row.Window.Start!.Value,
            RentalStatusExtensions.FromDbString(r.Status), invoice?.Status == "paid" ? price.Total : 0m,
            price.Rent + price.Vat, CancellationActor.Customer) : null;
        return new CustomerRentalDetail(r.Id, r.Status, r.RentalType, r.IsSubscription,
            new CustomerVehicleDto(row.Vehicle.Id, row.Vehicle.Brand ?? "Unknown", row.Vehicle.Model ?? "Unknown", row.Vehicle.MainImgUrl, row.Vehicle.Color),
            row.Facility == null ? null : new CustomerFacilityDto(row.Facility.Id, row.Facility.Name, row.Facility.Phone),
            row.Window.Start!.Value, row.Window.End!.Value, row.Window.HasDriver,
            new CustomerPickupDto("Facility", row.Facility?.Name), driver, price, invoice, inspections, settlement, timeline,
            canCancel, cancellation, NextAction(r.Status, r.ConfirmedAt != null, invoice?.Status, r.IsSubscription),
            row.OrderId, SubscriptionHours(row.Window), r.ExpireAt);
    }

    private static decimal? SubscriptionHours(RentalWindow window) => window.Rental.IsSubscription
        ? (decimal)(window.End!.Value - window.Start!.Value).Ticks / TimeSpan.TicksPerHour + window.Rental.OvertimeHours : null;
    private static CustomerInvoiceDto? Invoice(InvoicesRow? invoice) => invoice == null ? null
        : new CustomerInvoiceDto(invoice.Id, invoice.InvoiceNumber, invoice.Status, invoice.Amount);

    private static string? NextAction(string status, bool confirmed, string? invoiceStatus, bool subscription) => status switch
    {
        "pending" => "WAITING_CONFIRMATION",
        "pre_inspection" when !confirmed => "WAITING_CONFIRMATION",
        "pre_inspection" when invoiceStatus != "paid" && !subscription => "PAYMENT_REQUIRED",
        "pre_inspection" => "PREPARING_VEHICLE",
        "renting" => "ENJOY_YOUR_TRIP",
        "post_inspection" => "PROCESSING_RETURN",
        "completed" => "COMPLETED",
        "cancelled" => "CANCELLED",
        _ => null
    };

    public async Task<PagedResult<OperationalRentalSummary>> ListOperationalRentalsAsync(OperationalFilter filter, CancellationToken ct)
    {
        RentalQueryValidation.Operational(filter);
        await using var db = VehicleRentalDbContext.Create(dataSource);
        var rentals = Windows(db);
        if (filter.HandoverStatus == "pending") rentals = rentals.Where(x => x.Rental.Status == "pre_inspection");
        else if (filter.HandoverStatus == "completed") rentals = rentals.Where(x => x.Rental.StartedAt != null &&
            (x.Rental.Status == "renting" || x.Rental.Status == "post_inspection" || x.Rental.Status == "completed"));
        else if (filter.HandoverStatus == "all") rentals = rentals.Where(x => x.Rental.Status == "pre_inspection" || x.Rental.StartedAt != null &&
            (x.Rental.Status == "renting" || x.Rental.Status == "post_inspection" || x.Rental.Status == "completed"));
        var now = DateTime.SpecifyKind(timeProvider.GetUtcNow().UtcDateTime, DateTimeKind.Unspecified);
        var status = filter.Status?.Trim().ToLowerInvariant();
        if (status == "overdue") rentals = rentals.Where(x => x.Rental.Status == "renting" && x.Rental.ReturnedAt == null && x.End < now);
        else if (!string.IsNullOrWhiteSpace(status)) rentals = rentals.Where(x => x.Rental.Status == status);
        if (filter.CustomerId is { } customerId) rentals = rentals.Where(x => x.Rental.CustomerId == customerId);
        if (filter.VehicleId is { } vehicleId) rentals = rentals.Where(x => x.Rental.VehicleId == vehicleId);
        if (filter.From is { } from)
        {
            var start = DateTime.SpecifyKind(from.Kind == DateTimeKind.Local ? from.ToUniversalTime() : from, DateTimeKind.Unspecified);
            rentals = rentals.Where(x => x.Start >= start);
        }
        if (filter.To is { } to)
        {
            var end = DateTime.SpecifyKind(to.Kind == DateTimeKind.Local ? to.ToUniversalTime() : to, DateTimeKind.Unspecified);
            rentals = rentals.Where(x => x.End <= end);
        }
        var total = await rentals.CountAsync(ct);
        var offset = ((long)filter.Page - 1) * filter.PageSize;
        if (offset >= total) return new([], total, filter.Page, filter.PageSize);
        var items = await (from x in rentals
            join c in db.Set<CustomersRow>() on x.Rental.CustomerId equals c.Id
            join u in db.Set<UsersRow>() on c.UserId equals u.Id
            join v in db.Set<VehiclesRow>() on x.Rental.VehicleId equals v.Id
            join a0 in db.Set<VehicleAssignmentsRow>().Where(a => a.Status == "assigned") on x.Rental.Id equals a0.RentalId into assignments
            from a in assignments.DefaultIfEmpty()
            join s0 in db.Set<StaffsRow>() on a.DriverStaffId equals s0.Id into staffs
            from s in staffs.DefaultIfEmpty()
            join du0 in db.Set<UsersRow>() on s.UserId equals du0.Id into drivers
            from du in drivers.DefaultIfEmpty()
            orderby x.Rental.Id descending
            select new OperationalRentalSummary(x.Rental.Id, x.Rental.CustomerId, u.Fullname ?? u.Email, u.Email,
                v.Id, v.LicensePlate, v.Model ?? "Unknown", x.Rental.Status, x.Start!.Value, x.End!.Value,
                x.Rental.TotalPrice, x.HasDriver, du.Fullname ?? du.Email, x.Rental.RentalType,
                db.Set<RentalOrdersRow>().Where(o => o.RentalId == x.Rental.Id).Select(o => (int?)o.Id).FirstOrDefault(),
                x.Rental.Status == "renting" && x.Rental.ReturnedAt == null && x.End < now, x.Rental.ExpireAt))
            .Skip((int)offset).Take(filter.PageSize).ToListAsync(ct);
        return new(items, total, filter.Page, filter.PageSize);
    }

    public async Task<OperationalRentalDetail?> GetOperationalDetailAsync(int rentalId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        var row = await (from x in Windows(db)
            join c in db.Set<CustomersRow>() on x.Rental.CustomerId equals c.Id
            join u in db.Set<UsersRow>() on c.UserId equals u.Id
            join v in db.Set<VehiclesRow>() on x.Rental.VehicleId equals v.Id
            join f0 in db.Set<FacilitiesRow>() on v.FacilityId equals f0.Id into facilities
            from f in facilities.DefaultIfEmpty()
            join p0 in db.Set<RentalPoliciesRow>() on x.Rental.PolicyId equals p0.Id into policies
            from p in policies.DefaultIfEmpty()
            join a0 in db.Set<VehicleAssignmentsRow>().Where(a => a.Status == "assigned") on x.Rental.Id equals a0.RentalId into assignments
            from a in assignments.DefaultIfEmpty()
            join s0 in db.Set<StaffsRow>() on a.DriverStaffId equals s0.Id into staffs
            from s in staffs.DefaultIfEmpty()
            join du0 in db.Set<UsersRow>() on s.UserId equals du0.Id into drivers
            from du in drivers.DefaultIfEmpty()
            join i0 in db.Set<InvoicesRow>().Where(i => i.Kind == "rental") on x.Rental.Id equals i0.RentalId into invoices
            from i in invoices.DefaultIfEmpty()
            where x.Rental.Id == rentalId
            select new { Window = x, User = u, Vehicle = v, FacilityName = f.Name, DriverName = du.Fullname ?? du.Email,
                OvertimeFeeRate = (decimal?)p.OvertimeFeeRate ?? 0m, VatRate = (decimal?)p.VatRate ?? 10m, Invoice = i,
                OrderId = db.Set<RentalOrdersRow>().Where(o => o.RentalId == rentalId).Select(o => (int?)o.Id).FirstOrDefault(),
                HasPassingPreInspection = db.Set<InspectionsRow>().Any(ip => ip.RentalId == rentalId && ip.VehicleId == x.Rental.VehicleId && ip.InspectionType == "pre_rental" && ip.IsOk == true),
                HasPostInspection = db.Set<InspectionsRow>().Any(ip => ip.RentalId == rentalId && ip.VehicleId == x.Rental.VehicleId && ip.InspectionType == "post_rental") }).FirstOrDefaultAsync(ct);
        if (row == null) return null;
        var r = row.Window.Rental;
        var entity = new RentalLifecycleEntity(r.Id, r.CustomerId, r.VehicleId, r.PolicyId, r.CustomerSubscriptionId,
            r.IsSubscription, r.RentalType, RentalStatusExtensions.FromDbString(r.Status), r.VatPrice, r.DepositPrice,
            r.OvertimePrice, r.TotalPrice, row.Window.Start!.Value, row.Window.End!.Value, row.Window.HasDriver,
            row.Window.HourlyPrice, row.Window.MonthlyPrice, row.OvertimeFeeRate, row.VatRate,
            r.ConfirmedAt, r.StartedAt, r.ReturnedAt, r.CompletedAt, r.CancelledAt, r.CancelledBy, r.CancelReason, r.OvertimeHours,
            ExpireAt: r.ExpireAt, ExpiredAt: r.ExpiredAt);
        return new OperationalRentalDetail(entity, row.User.Fullname ?? row.User.Email, row.User.Email, row.User.Phone ?? "",
            row.Vehicle.Brand ?? "Unknown", row.Vehicle.Model ?? "Unknown", row.Vehicle.LicensePlate,
            row.FacilityName ?? "Main Facility", row.DriverName, Invoice(row.Invoice), await Inspections(db, rentalId, ct),
            await History(db, rentalId, ct), await Settlement(db, rentalId, ct), row.OrderId, row.HasPassingPreInspection, row.HasPostInspection);
    }

    private static async Task<List<CustomerInspectionDto>> Inspections(VehicleRentalDbContext db, int rentalId, CancellationToken ct) =>
        await db.Set<InspectionsRow>().Where(i => i.RentalId == rentalId).OrderBy(i => i.Id)
            .Select(i => new CustomerInspectionDto(i.InspectionType, i.IsOk!.Value, i.OdometerKm, i.FuelBatteryLevel,
                i.DamageNotes, i.ImgUrls, i.InspectedAt!.Value)).ToListAsync(ct);

    private static async Task<List<RentalStatusHistoryEntry>> History(VehicleRentalDbContext db, int rentalId, CancellationToken ct) =>
        await db.Set<RentalStatusHistoryRow>().Where(h => h.RentalId == rentalId).OrderBy(h => h.Id)
            .Select(h => new RentalStatusHistoryEntry(h.Id, h.RentalId, h.FromStatus, h.ToStatus, h.ActorUserId,
                h.ActorEmail, h.ActorRole, h.Reason, h.CreatedAt)).ToListAsync(ct);

    private static async Task<RentalSettlementDto?> Settlement(VehicleRentalDbContext db, int rentalId, CancellationToken ct) =>
        await db.Set<RentalSettlementsRow>().Where(s => s.RentalId == rentalId)
            .Select(s => new RentalSettlementDto(s.RentalId, s.ScheduledEnd, s.ReturnedAt, s.OvertimeMinutes, s.OvertimeHours,
                s.OvertimeAmount, s.OvertimeVat, s.DepositHeld, s.DepositDeducted, s.ShortfallInvoiceId, s.RefundPaymentId,
                s.DamageReviewRequired, s.CreatedAt, null, null)).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<OverdueRentalItem>> ListOverdueRentalsAsync(CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        var now = DateTime.SpecifyKind(timeProvider.GetUtcNow().UtcDateTime, DateTimeKind.Unspecified);
        var windows = Windows(db);
        var rows = await (from x in windows
            join v in db.Set<VehiclesRow>() on x.Rental.VehicleId equals v.Id
            join c in db.Set<CustomersRow>() on x.Rental.CustomerId equals c.Id
            join u in db.Set<UsersRow>() on c.UserId equals u.Id
            where x.Rental.Status == "renting" && x.End < now
            orderby x.End
            select new { RentalId = x.Rental.Id, VehicleId = v.Id, v.LicensePlate, VehicleModel = v.Model ?? "Unknown",
                CustomerId = c.Id, CustomerName = u.Fullname ?? u.Email, CustomerPhone = u.Phone, ScheduledEnd = x.End!.Value,
                NextId = windows.Where(next => next.Rental.VehicleId == v.Id && next.Rental.Id != x.Rental.Id &&
                    next.Rental.Status != "cancelled" && next.Rental.Status != "completed" && next.Rental.Status != "expired" && next.Start >= x.End)
                    .OrderBy(next => next.Start).Select(next => (int?)next.Rental.Id).FirstOrDefault() }).ToListAsync(ct);
        return rows.Select(x => new OverdueRentalItem(x.RentalId, x.VehicleId, x.LicensePlate, x.VehicleModel,
            x.CustomerId, x.CustomerName, x.CustomerPhone, x.ScheduledEnd,
            checked((int)Math.Round((now - x.ScheduledEnd).TotalMinutes, MidpointRounding.AwayFromZero)), x.NextId)).ToList();
    }
}
