using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Lifecycle;
using Xunit;

namespace VehicleRental.Tests;

public sealed class RentalEditingTests
{
    private static readonly DateTime Start = new(2032, 1, 1, 8, 0, 0, DateTimeKind.Utc);
    private static Rental Original => new(1, 2, 3, "hourly", "pending", false, 20m, 40m, 0, 220m, Start, Start.AddHours(2), 100m);

    [Theory]
    [InlineData("renting")]
    [InlineData("post_inspection")]
    [InlineData("completed")]
    [InlineData("cancelled")]
    public void Started_or_final_rentals_are_immutable(string status)
        => Assert.Equal("RENTAL_NOT_EDITABLE", Assert.Throws<ConflictException>(() => RentalEditingRules.RequireEditable(status, null)).Code);

    [Fact]
    public void Started_timestamp_blocks_edit_even_if_status_is_stale()
        => Assert.Throws<ConflictException>(() => RentalEditingRules.RequireEditable("pending", Start));

    [Fact]
    public void Repricing_uses_existing_vat_and_deposit_snapshot_ratios()
    {
        var price = RentalEditingRules.Reprice(Original, new(3, Start, Start.AddHours(3), false), 100m);
        Assert.Equal(new RentalEditingPrice(330m, 30m, 60m, 100m), price);
        var replacement = RentalEditingRules.Reprice(Original, new(4, Start, Start.AddHours(2), false), 200m);
        Assert.Equal(new RentalEditingPrice(440m, 40m, 80m, 200m), replacement);
    }

    [Fact]
    public void Longterm_rounds_up_full_30_day_periods()
    {
        var rental = Original with { RentalType = "longterm", EndTime = Start.AddDays(30), HourlyPriceSnapshot = null, MonthlyPriceSnapshot = 1000m, TotalPrice = 1100m, VatPrice = 100m, DepositPrice = 200m };
        var price = RentalEditingRules.Reprice(rental, new(3, Start, Start.AddDays(31), false), 1000m);
        Assert.Equal(new RentalEditingPrice(2200m, 200m, 400m, 1000m), price);
    }

    [Fact]
    public void Invalid_monetary_snapshot_is_rejected()
        => Assert.Equal("RENTAL_PRICING_UNAVAILABLE", Assert.Throws<ConflictException>(() => RentalEditingRules.Reprice(Original with { TotalPrice = 0 }, new(3, Start, Start.AddHours(3), false), 100m)).Code);

    [Fact]
    public void Unsupported_amount_is_validation_error()
        => Assert.Equal("INVALID_RENTAL_EDIT", Assert.Throws<BusinessRuleException>(() => RentalEditingRules.Reprice(Original, new(3, Start, DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc), false), decimal.MaxValue)).Code);

    [Fact]
    public async Task Service_requires_every_put_field_and_utc_before_repository_call()
    {
        var repository = new RecordingRepository();
        var service = new RentalEditingService(repository);
        var invalid = new[]
        {
            new UpdateRentalRequest(null, Start, Start.AddHours(2), false),
            new UpdateRentalRequest(3, null, Start.AddHours(2), false),
            new UpdateRentalRequest(3, Start, null, false),
            new UpdateRentalRequest(3, Start, Start.AddHours(2), null),
            new UpdateRentalRequest(3, Start, Start, false),
            new UpdateRentalRequest(3, DateTime.SpecifyKind(Start, DateTimeKind.Unspecified), Start.AddHours(2), false)
        };
        foreach (var request in invalid)
            await Assert.ThrowsAsync<BusinessRuleException>(() => service.UpdateAsync(1, request, default));
        Assert.Null(repository.Command);
        await service.UpdateAsync(1, new(3, Start, Start.AddHours(2), false), default);
        Assert.Equal(new UpdateRentalCommand(3, Start, Start.AddHours(2), false), repository.Command);
    }

    private sealed class RecordingRepository : IRentalEditingRepository
    {
        public UpdateRentalCommand? Command { get; private set; }
        public Task<Rental> UpdateBeforePickupAsync(int id, UpdateRentalCommand command, CancellationToken ct)
        {
            Command = command;
            return Task.FromResult(Original);
        }
    }
}
