using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.Features.Rentals;
using Car_Rental_System_BE.Infrastructure.Features.Rentals;
using Xunit;

namespace VehicleRental.Tests;

public sealed class RentalContractTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Longterm_contract_roundtrips_is_immutable_and_blocks_overlapping_booking()
    {
        var editing = new NpgsqlRentalEditingRepository(db.DataSource);
        var bookings = new NpgsqlRentalBookingRepository(db.DataSource);
        var (_, email) = await Seed.CustomerAsync(db.DataSource);
        var (_, other) = await Seed.CustomerAsync(db.DataSource);
        var vehicle = await Seed.VehicleAsync(db.DataSource);
        var start = DateTime.UtcNow.AddDays(1);
        var rental = await bookings.CreateRentalAsync(email, vehicle, null, "longterm", start, start.AddDays(30), false, null, false, default);
        var signedAt = DateTime.UtcNow.AddMinutes(-1);
        var saved = await editing.SaveRentalContractAsync(rental.Id, "https://example.com/contract.pdf", signedAt, default);
        Assert.Equal(1000m, saved.MonthlyPriceSnapshot);
        Assert.Null(saved.HourlyPriceSnapshot);
        Assert.Equal(saved, await editing.SaveRentalContractAsync(rental.Id, saved.ContractLink!, signedAt, default));
        Assert.Equal(saved, await bookings.GetRentalForUserAsync(rental.Id, email, false, true, false, default));
        Assert.Null(await bookings.GetRentalForUserAsync(rental.Id, other, false, true, false, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => editing.SaveRentalContractAsync(rental.Id, "https://example.com/replaced.pdf", signedAt, default));
        await Assert.ThrowsAsync<ConflictException>(() => bookings.CreateRentalAsync(email, vehicle, null, "hourly", start, start.AddHours(2), false, null, false, default));
        await Seed.ScalarAsync(db.DataSource, "UPDATE vehicles SET rental_price_longterm=9999 WHERE id=$1", vehicle);
        Assert.Equal(1000m, (await bookings.GetRentalAsync(rental.Id, default))!.MonthlyPriceSnapshot);
    }

    [Fact]
    public async Task Contract_rejects_hourly_missing_rental_invalid_url_and_dates()
    {
        var editing = new NpgsqlRentalEditingRepository(db.DataSource);
        var bookings = new NpgsqlRentalBookingRepository(db.DataSource);
        var rental = await Scenarios.CreateDirectRentalAsync(db.DataSource);
        var now = DateTime.UtcNow.AddMinutes(-1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => editing.SaveRentalContractAsync(rental.RentalId, "https://example.com/a", now, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => editing.SaveRentalContractAsync(int.MaxValue, "https://example.com/a", now, default));
        await Assert.ThrowsAsync<ArgumentException>(() => editing.SaveRentalContractAsync(rental.RentalId, "file:///secret", now, default));
        await Assert.ThrowsAsync<ArgumentException>(() => editing.SaveRentalContractAsync(rental.RentalId, "https://example.com/a", default, default));
        await Assert.ThrowsAsync<ArgumentException>(() => editing.SaveRentalContractAsync(rental.RentalId, "https://example.com/a", now.AddDays(1), default));
        var detail = (await bookings.GetRentalAsync(rental.RentalId, default))!;
        Assert.Equal(100m, detail.HourlyPriceSnapshot);
        Assert.Equal(20m, detail.VatPrice);
        Assert.Equal(40m, detail.DepositPrice);
        Assert.False(detail.HasDriver);
        // The retired legacy repository refused this with InvalidOperationException("Invalid rental state
        // transition."); the lifecycle refuses the same transition (pending -> renting) with the documented code.
        var illegal = await Assert.ThrowsAsync<ConflictException>(() =>
            Scenarios.Lifecycle(db.DataSource).StartAsync(detail.Id, Scenarios.StaffActor, default));
        Assert.Equal("ILLEGAL_TRANSITION", illegal.Code);
    }
}
