namespace Car_Rental_System_BE.Infrastructure.Persistence;

internal static class Sql
{
    /// <summary>PostgreSQL timestamp values carry UTC wall-clock time with Kind=Unspecified.</summary>
    public static DateTime Ts(DateTime value) => DateTime.SpecifyKind(
        value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value, DateTimeKind.Unspecified);
    public static DateTime? Ts(DateTime? value) => value is { } v ? Ts(v) : null;
}
