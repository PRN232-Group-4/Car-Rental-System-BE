using NpgsqlTypes;

namespace Car_Rental_System_BE.Infrastructure.Persistence;

internal static class Sql
{
    /// <summary>Giá trị timestamp của PostgreSQL mang giờ UTC với Kind=Unspecified.</summary>
    public static DateTime Ts(DateTime value) => DateTime.SpecifyKind(
        value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value, DateTimeKind.Unspecified);
    public static DateTime? Ts(DateTime? value) => value is { } v ? Ts(v) : null;

    /// <summary>Khoảng nửa mở <c>[from, to)</c> trên cột <c>tstzrange</c>/<c>tsrange</c>, đã chuẩn hoá múi giờ như <see cref="Ts"/>.</summary>
    public static NpgsqlRange<DateTime> Range(DateTime from, DateTime to) => new(Ts(from), true, Ts(to), false);
}
