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
