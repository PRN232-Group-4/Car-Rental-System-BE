using System.ComponentModel.DataAnnotations;

namespace Car_Rental_System_BE.Application.Features.Audit;

public sealed class AuditLogQuery
{
    [StringLength(255)] public string? Search { get; init; }
    [StringLength(40)] public string? Action { get; init; }
    [Range(1, int.MaxValue)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
}

public sealed record AuditLogItem(string Id, DateTime Timestamp, int ActorId, string? ActorEmail,
    string? ActorName, string Action, int TargetUserId, string? TargetUser, string? Details,
    string? IpAddress, string Status);

public sealed record AuditLogPage(IReadOnlyList<AuditLogItem> Items, int Page, int PageSize, int TotalCount);
