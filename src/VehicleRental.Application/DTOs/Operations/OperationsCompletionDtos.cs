using System.ComponentModel.DataAnnotations;

namespace Car_Rental_System_BE.Application.DTOs.Operations;

public sealed record OperationsPage<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
public sealed record OperationsPagination
{
    [Range(1, int.MaxValue)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
    public void Validate()
    {
        if (Page < 1 || PageSize is < 1 or > 100) throw new ArgumentException("page must be positive and pageSize must be between 1 and 100.");
    }
}
public sealed record RatingReply(string Text, string By, DateTime At);
public sealed record RatingModerationRequest([param: Required, RegularExpression("^(visible|hidden|reported)$")] string Status,
    [param: Required, StringLength(2000, MinimumLength=1)] string Reason);
public sealed record RatingReplyRequest([param: Required, StringLength(4000, MinimumLength=1)] string Text);
public sealed record ReportStatusRequest([param: Required, RegularExpression("^(in_progress|rejected)$")] string Status,
    [param: Required, StringLength(2000, MinimumLength=1)] string Reason);
public sealed record ReportCustomer(int Id, string? FullName, string? Phone, string Email);
public sealed record ReportRental(int Id, string? VehicleName, string? LicensePlate, DateTime? Start, DateTime? End, string Status, string? DriverName);
public sealed record ReportMaintenance(string? Type, string? Status, decimal? Cost, DateOnly? ScheduledDate);
public sealed record ReportHistory(long Id, string? FromStatus, string? Status, string? Reason, string? By, DateTime At);
public sealed record ReportNote(long Id, string Text, string? By, DateTime At);
