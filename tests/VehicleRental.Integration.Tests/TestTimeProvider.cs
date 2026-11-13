namespace VehicleRental.Tests;

internal sealed class SettableTimeProvider(DateTime initialUtc) : TimeProvider
{
    public DateTime UtcNow { get; set; } = initialUtc;

    public void Advance(TimeSpan delta) => UtcNow = UtcNow.Add(delta);
    public void Set(DateTime utc) => UtcNow = utc;

    public override DateTimeOffset GetUtcNow() => new(UtcNow, TimeSpan.Zero);
}
