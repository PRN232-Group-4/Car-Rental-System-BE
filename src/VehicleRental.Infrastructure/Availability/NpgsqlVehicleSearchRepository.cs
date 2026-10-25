using System.Text.RegularExpressions;
using Car_Rental_System_BE.Application.Availability;
using Car_Rental_System_BE.Domain.Availability;
using Car_Rental_System_BE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Car_Rental_System_BE.Infrastructure.Availability;

public sealed class NpgsqlVehicleSearchRepository(NpgsqlDataSource dataSource) : IVehicleSearchRepository
{
    public async Task<VehicleSearchResult> SearchAsync(VehicleSearchQuery query, int bufferMinutes, bool? driverAvailable, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        var matches =
            from v in db.Set<VehiclesRow>()
            join t in db.Set<VehicleTypesRow>() on v.VehicleTypeId equals t.Id
            join f0 in db.Set<FacilitiesRow>() on v.FacilityId equals f0.Id into facilities
            from f in facilities.DefaultIfEmpty()
            join cd0 in db.Set<CarDetailRow>() on v.Id equals cd0.VehicleId into cars
            from cd in cars.DefaultIfEmpty()
            join md0 in db.Set<MotorbikeDetailRow>() on v.Id equals md0.VehicleId into bikes
            from md in bikes.DefaultIfEmpty()
            join ec0 in db.Set<ElectricCarDetailRow>() on v.Id equals ec0.VehicleId into electricCars
            from ec in electricCars.DefaultIfEmpty()
            join em0 in db.Set<ElectricMotorbikeDetailRow>() on v.Id equals em0.VehicleId into electricBikes
            from em in electricBikes.DefaultIfEmpty()
            let fuel = t.FuelType == null ? null : t.FuelType!.Trim().ToLower()
                .Replace("Đ", "đ").Replace("Ă", "ă").Replace("Ệ", "ệ").Replace("Ầ", "ầ")
            let type = t.Type != null && t.Type!.Trim() != "" ? t.Type!.Trim().ToLower()
                : cd != null || ec != null ? "car" : md != null || em != null ? "motorbike" : null
            let transmission = cd!.Transmission! == null ? null :
                EF.Functions.ILike(cd!.Transmission!, "%cvt%") ? "cvt" :
                cd!.Transmission!.Trim().ToLower() == "automatic" || cd!.Transmission!.Trim().ToLower() == "at" || EF.Functions.ILike(cd!.Transmission!, "%tự động%") ? "automatic" :
                cd!.Transmission!.Trim().ToLower() == "manual" || cd!.Transmission!.Trim().ToLower() == "mt" || EF.Functions.ILike(cd!.Transmission!, "%số sàn%") ? "manual" : cd!.Transmission!.Trim().ToLower()
            where v.Status == "available" && v.AllowedLicenseTypes.Any(license => Regex.IsMatch(license, "[^[:space:]]"))
                && !db.Set<RentalsRow>().Any(r => r.VehicleId == v.Id && (r.Status == "renting" || r.Status == "post_inspection"))
            select new
            {
                Vehicle = v, TypeId = t.Id, TypeName = t.Name, t.FuelType, Type = type,
                FacilityName = f.Name, SeatCount = cd.SeatCount, RawTransmission = cd!.Transmission!, Transmission = transmission,
                BikeType = md.BikeType, HasStorageBox = md.HasStorageBox,
                MaxRange = ec.MaxRangeKm ?? em.MaxRangeKm, BatteryCapacity = ec.BatteryCapacity ?? em.BatteryCapacity,
                IsElectric = ec != null || em != null || fuel == "electric" || fuel == "điện" || fuel == "thuần điện",
                Fuel = fuel,
                Price = query.RentalType == "longterm" ? v.RentalPriceLongterm : v.RentalPriceHourly,
                SearchText = string.Join(" ", new[] { v.Brand, v.Model, v.YearOfManufacture.ToString(), f.Name, v.CurrentLocation }).ToLower(),
                Name = string.Join(" ", new[] { v.Brand, v.Model }).ToLower()
            };

        if (query.Category is { } category)
        {
            var categoryType = category.EndsWith("motorbike", StringComparison.Ordinal) ? "motorbike" : "car";
            var electric = category.StartsWith("electric_", StringComparison.Ordinal);
            matches = matches.Where(x => x.Type == categoryType && x.IsElectric == electric);
        }
        if (query.Window is { } window)
        {
            var bookingRange = Range(window.From, window.To.AddMinutes(bufferMinutes));
            var maintenanceRange = Range(window.From, window.To);
            matches = matches.Where(x => !db.Set<VehicleBookingsRow>().Any(b => b.VehicleId == x.Vehicle.Id && b.IsBlocking && b.Period!.Value.Overlaps(bookingRange))
                && !db.Set<VVehicleMaintenanceBlocksRow>().Any(b => b.VehicleId == x.Vehicle.Id && b.BlockedRange.Overlaps(maintenanceRange)));
        }
        if (query.VehicleTypeId is { } typeId) matches = matches.Where(x => x.Vehicle.VehicleTypeId == typeId);
        if (query.FacilityId is { } facilityId) matches = matches.Where(x => x.Vehicle.FacilityId == facilityId);
        if (!string.IsNullOrWhiteSpace(query.Brand))
        {
            var brand = query.Brand.Trim().ToLowerInvariant();
            matches = matches.Where(x => x.Vehicle.Brand != null && x.Vehicle.Brand.ToLower() == brand);
        }
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var pattern = $"%{EscapeLike(query.Q.Trim().ToLowerInvariant())}%";
            matches = matches.Where(x => EF.Functions.Like(x.SearchText, pattern));
        }
        if (query.MinPrice is { } minPrice) matches = matches.Where(x => x.Price >= minPrice);
        if (query.MaxPrice is { } maxPrice) matches = matches.Where(x => x.Price <= maxPrice);
        if (query.SeatCount is { } seats) matches = matches.Where(x => x.SeatCount >= seats);
        if (!string.IsNullOrWhiteSpace(query.Transmission))
        {
            var transmission = query.Transmission.Trim().ToLowerInvariant();
            matches = transmission == "automatic"
                ? matches.Where(x => x.Transmission == "automatic" || x.Transmission == "cvt")
                : matches.Where(x => x.RawTransmission != null && x.RawTransmission.Trim().ToLower() == transmission || x.Transmission == transmission);
        }
        if (!string.IsNullOrWhiteSpace(query.BikeType))
        {
            var bikeType = query.BikeType.Trim().ToLowerInvariant();
            matches = matches.Where(x => x.BikeType != null && x.BikeType.ToLower() == bikeType);
        }
        if (query.MinRangeKm is { } minRange) matches = matches.Where(x => x.MaxRange >= minRange);
        if (query.YearFrom is { } year) matches = matches.Where(x => x.Vehicle.YearOfManufacture >= year);
        if (!string.IsNullOrWhiteSpace(query.FuelType))
        {
            var fuel = query.FuelType.Trim().ToLowerInvariant();
            matches = fuel switch
            {
                "electric" or "điện" or "thuần điện" => matches.Where(x => x.IsElectric),
                "petrol" or "gasoline" or "xăng" => matches.Where(x => x.Fuel == "petrol" || x.Fuel == "gasoline" || x.Fuel == "xăng"),
                _ => matches.Where(x => x.Fuel == fuel)
            };
        }
        var total = await matches.CountAsync(ct);
        var ordered = query.Sort switch
        {
            "price_asc" => matches.OrderBy(x => x.Price == null).ThenBy(x => x.Price).ThenBy(x => x.Vehicle.Id),
            "price_desc" => matches.OrderBy(x => x.Price == null).ThenByDescending(x => x.Price).ThenBy(x => x.Vehicle.Id),
            "newest" => matches.OrderBy(x => x.Vehicle.YearOfManufacture == null).ThenByDescending(x => x.Vehicle.YearOfManufacture).ThenByDescending(x => x.Vehicle.Id),
            "name_asc" => matches.OrderBy(x => x.Name).ThenBy(x => x.Vehicle.Id),
            _ => matches.OrderByDescending(x => x.Vehicle.Id)
        };
        var offset = ((long)query.Page - 1) * query.PageSize;
        var rows = offset >= total ? [] : await ordered.Skip((int)offset).Take(query.PageSize).ToListAsync(ct);
        double? durationHours = query.Window?.Duration.TotalHours;
        var items = rows.Select(x => new VehicleSearchItem(x.Vehicle.Id,
            new VehicleTypeRef(x.TypeId, x.TypeName, x.FuelType, x.Type), x.Vehicle.Brand, x.Vehicle.Model,
            x.Vehicle.YearOfManufacture, x.Vehicle.Color, x.Vehicle.MainImgUrl, x.Vehicle.CurrentLocation,
            x.Vehicle.FacilityId is { } id && x.FacilityName != null ? new FacilityRef(id, x.FacilityName) : null,
            x.Vehicle.RentalPriceHourly, x.Vehicle.RentalPriceLongterm,
            new VehicleSummary(x.SeatCount, x.RawTransmission, x.BikeType, x.HasStorageBox, x.MaxRange, x.BatteryCapacity),
            new VehicleBadges(x.IsElectric, driverAvailable, x.Vehicle.RentalPriceHourly != null), durationHours)
            { AllowedLicenseTypes = x.Vehicle.AllowedLicenseTypes }).ToList();
        AppliedWindow? appliedWindow = query.Window is { } applied ? new(applied.From, applied.To, bufferMinutes) : null;
        return new VehicleSearchResult(items, query.Page, query.PageSize, total, appliedWindow);
    }

    public async Task<ActiveRentalPolicy?> GetActivePolicyAsync(int vehicleTypeId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await db.Set<RentalPoliciesRow>().Where(p => p.IsActive && (p.VehicleTypeId == vehicleTypeId || p.VehicleTypeId == null))
            .OrderByDescending(p => p.VehicleTypeId != null).ThenByDescending(p => p.Id)
            .Select(p => new ActiveRentalPolicy(p.Id, p.VehicleTypeId, p.Name, p.VatRate, p.DepositPercentage, p.OvertimeFeeRate)).FirstOrDefaultAsync(ct);
    }

    public async Task<bool> VehicleExistsAsync(int vehicleId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await db.Set<VehiclesRow>().AnyAsync(v => v.Id == vehicleId, ct);
    }

    public async Task<IReadOnlyList<BusySlot>> ListBusySlotsAsync(int vehicleId, TimeRange window, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        var range = Range(window.From, window.To);
        var bookings = await db.Set<VehicleBookingsRow>()
            .Where(b => b.VehicleId == vehicleId && b.IsBlocking && b.Period!.Value.Overlaps(range))
            .OrderBy(b => b.StartsAt).Select(b => new { Start = b.StartsAt, End = b.Period!.Value.UpperBound }).ToListAsync(ct);
        var maintenance = await db.Set<VVehicleMaintenanceBlocksRow>()
            .Where(b => b.VehicleId == vehicleId && b.BlockedRange.Overlaps(range))
            .OrderBy(b => b.BlockedRange.LowerBound).Select(b => b.BlockedRange).ToListAsync(ct);
        var slots = bookings.Select(b => new BusySlot(b.Start < window.From ? window.From : b.Start, b.End > window.To ? window.To : b.End, "booking")).ToList();
        slots.AddRange(maintenance.Select(b => new BusySlot(
            b.LowerBoundInfinite || b.LowerBound < window.From ? window.From : b.LowerBound,
            b.UpperBoundInfinite || b.UpperBound > window.To ? window.To : b.UpperBound, "maintenance")));
        return slots.Where(s => s.To > s.From).OrderBy(s => s.From).ToList();
    }

    public async Task<IReadOnlyList<AffectedRental>> ListAffectedRentalsAsync(int vehicleId, DateTime nowUtc, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        var now = Timestamp(nowUtc);
        return await (from r in db.Set<RentalsRow>()
            join c in db.Set<CustomersRow>() on r.CustomerId equals c.Id
            join u in db.Set<UsersRow>() on c.UserId equals u.Id
            join h0 in db.Set<HourRentalDetailRow>() on r.Id equals h0.RentalId into hours
            from h in hours.DefaultIfEmpty()
            join l0 in db.Set<LongtermRentalContractRow>() on r.Id equals l0.RentalId into contracts
            from l in contracts.DefaultIfEmpty()
            let start = h.StartTime ?? l.StartTime
            let end = h.EndTime ?? l.EndTime
            where r.VehicleId == vehicleId && (r.Status == "pending" || r.Status == "pre_inspection" || r.Status == "renting")
                && (start >= now || db.Set<VVehicleMaintenanceBlocksRow>().Any(b => b.VehicleId == r.VehicleId && !b.BlockedRange.IsEmpty && (b.BlockedRange.LowerBoundInfinite || b.BlockedRange.LowerBound < end) && (b.BlockedRange.UpperBoundInfinite || b.BlockedRange.UpperBound > start)))
            orderby start, r.Id
            select new AffectedRental(r.Id, u.Fullname ?? u.Email, start!.Value, end!.Value, r.Status)).ToListAsync(ct);
    }

    internal static DateTime Timestamp(DateTime value) => DateTime.SpecifyKind(value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value, DateTimeKind.Unspecified);
    internal static NpgsqlRange<DateTime> Range(DateTime from, DateTime to) => new(Timestamp(from), true, Timestamp(to), false);
    private static string EscapeLike(string s) => s.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
}
