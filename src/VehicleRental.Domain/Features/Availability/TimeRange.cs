namespace Car_Rental_System_BE.Domain.Features.Availability;

/// <summary>Half-open UTC interval <c>[From, To)</c>. Values are plain instants; callers pass UTC.</summary>
public readonly record struct TimeRange
{
    public DateTime From { get; }
    public DateTime To { get; }

    public TimeRange(DateTime from, DateTime to)
    {
        if (from >= to) throw new ArgumentException("'from' must be earlier than 'to'.");
        From = from;
        To = to;
    }

    public TimeSpan Duration => To - From;

    /// <summary>Half-open overlap: ranges that merely touch do not overlap.</summary>
    public bool Overlaps(TimeRange other) => From < other.To && other.From < To;

    /// <summary>Extends the end by <paramref name="buffer"/> (the turnaround gap after a rental).</summary>
    public TimeRange WithBuffer(TimeSpan buffer)
        => buffer < TimeSpan.Zero ? throw new ArgumentOutOfRangeException(nameof(buffer)) : new(From, To + buffer);

    /// <summary>
    /// Symmetric conflict test between a stored rental and a requested window: either range, once extended by
    /// the buffer, must not overlap the other. Two rentals closer than <paramref name="buffer"/> conflict.
    /// </summary>
    public bool ConflictsWith(TimeRange other, TimeSpan buffer) => WithBuffer(buffer).Overlaps(other) || other.WithBuffer(buffer).Overlaps(this);
}
