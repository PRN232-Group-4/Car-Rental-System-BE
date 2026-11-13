using Car_Rental_System_BE.Domain.Features.Availability;
using Xunit;

namespace VehicleRental.Tests;

public sealed class DomainAvailabilityTests
{
    private static readonly DateTime T = new(2030, 1, 1, 8, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Buffer = TimeSpan.FromMinutes(60);

    [Fact]
    public void TimeRange_rejects_empty_and_reversed_ranges()
    {
        Assert.Throws<ArgumentException>(() => new TimeRange(T, T));
        Assert.Throws<ArgumentException>(() => new TimeRange(T.AddHours(1), T));
    }

    [Fact]
    public void Touching_ranges_do_not_overlap_but_one_minute_of_overlap_does()
    {
        var a = new TimeRange(T, T.AddHours(2));

        Assert.False(a.Overlaps(new TimeRange(T.AddHours(2), T.AddHours(3))));
        Assert.False(new TimeRange(T.AddHours(-1), T).Overlaps(a));
        Assert.True(a.Overlaps(new TimeRange(T.AddMinutes(119), T.AddHours(3))));
    }

    [Theory]
    [InlineData(59, true)]
    [InlineData(60, false)]
    [InlineData(61, false)]
    public void Buffer_conflict_is_symmetric_around_the_rental(int gapMinutes, bool conflict)
    {
        var rental = new TimeRange(T, T.AddHours(2));
        var after = new TimeRange(rental.To.AddMinutes(gapMinutes), rental.To.AddMinutes(gapMinutes + 60));
        var before = new TimeRange(rental.From.AddMinutes(-gapMinutes - 60), rental.From.AddMinutes(-gapMinutes));

        Assert.Equal(conflict, rental.ConflictsWith(after, Buffer));
        Assert.Equal(conflict, after.ConflictsWith(rental, Buffer));
        Assert.Equal(conflict, rental.ConflictsWith(before, Buffer));
    }

    [Fact]
    public void WithBuffer_extends_only_the_end_and_rejects_negative_buffer()
    {
        var range = new TimeRange(T, T.AddHours(1)).WithBuffer(Buffer);

        Assert.Equal(T, range.From);
        Assert.Equal(T.AddHours(2), range.To);
        Assert.Throws<ArgumentOutOfRangeException>(() => range.WithBuffer(TimeSpan.FromMinutes(-1)));
    }

    [Theory]
    [InlineData("pending", true)]
    [InlineData("pre_inspection", true)]
    [InlineData("renting", true)]
    [InlineData("post_inspection", true)]
    [InlineData("completed", false)]
    [InlineData("cancelled", false)]
    [InlineData("unknown", false)]
    [InlineData(null, false)]
    public void IsBlocking_covers_every_rental_status(string? status, bool expected)
        => Assert.Equal(expected, BookingStatusRules.IsBlocking(status));

    [Theory]
    [InlineData("available", true)]
    [InlineData("renting", true)]
    [InlineData("maintenance", false)]
    [InlineData("inactive", false)]
    [InlineData(null, false)]
    public void Only_available_and_renting_vehicles_are_bookable(string? status, bool expected)
        => Assert.Equal(expected, BookingStatusRules.IsBookableVehicleStatus(status));
}
