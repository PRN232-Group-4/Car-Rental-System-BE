namespace Car_Rental_System_BE.Infrastructure.Persistence;

internal static class Sql
{
    /// <summary>Giá trị timestamp của PostgreSQL mang giờ UTC với Kind=Unspecified.</summary>
    public static DateTime Ts(DateTime value) => DateTime.SpecifyKind(
        value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value, DateTimeKind.Unspecified);
    public static DateTime? Ts(DateTime? value) => value is { } v ? Ts(v) : null;
}
