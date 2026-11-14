namespace Car_Rental_System_BE.Domain.Features.Availability;

/// <summary>Khoảng UTC nửa mở <c>[From, To)</c>. Giá trị là thời điểm thuần; bên gọi truyền UTC.</summary>
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

    /// <summary>Chồng lấn nửa mở: hai khoảng chỉ chạm nhau thì không tính là chồng lấn.</summary>
    public bool Overlaps(TimeRange other) => From < other.To && other.From < To;

    /// <summary>Kéo dài điểm kết thúc thêm <paramref name="buffer"/> (khoảng nghỉ quay vòng sau một chuyến thuê).</summary>
    public TimeRange WithBuffer(TimeSpan buffer)
        => buffer < TimeSpan.Zero ? throw new ArgumentOutOfRangeException(nameof(buffer)) : new(From, To + buffer);

    /// <summary>
    /// Kiểm tra xung đột đối xứng giữa một đơn thuê đã lưu và khoảng thời gian được yêu cầu: mỗi khoảng, sau
    /// khi cộng thêm buffer, không được chồng lấn khoảng kia. Hai đơn thuê cách nhau ít hơn <paramref
    /// name="buffer"/> là xung đột.
    /// </summary>
    public bool ConflictsWith(TimeRange other, TimeSpan buffer) => WithBuffer(buffer).Overlaps(other) || other.WithBuffer(buffer).Overlaps(this);
}
