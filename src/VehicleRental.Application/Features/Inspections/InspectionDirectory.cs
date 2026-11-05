using System.ComponentModel.DataAnnotations;
using Car_Rental_System_BE.Application.DTOs.Operations;

namespace Car_Rental_System_BE.Application.Features.Inspections;

public sealed record InspectionListQuery
{
    [RegularExpression("^(pre_rental|post_rental)$")] public string? InspectionType { get; init; }
    [Range(1,int.MaxValue)] public int? RentalId { get; init; }
    [Range(1,int.MaxValue)] public int Page { get; init; } = 1;
    [Range(1,100)] public int PageSize { get; init; } = 20;
    public void Validate()
    {
        if (InspectionType is not null and not ("pre_rental" or "post_rental")) throw new ArgumentException("Loại kiểm tra phải là pre_rental hoặc post_rental.");
        if (RentalId is <= 0) throw new ArgumentException("Mã đơn thuê phải lớn hơn 0.");
        new OperationsPagination { Page=Page,PageSize=PageSize }.Validate();
    }
}

public sealed record InspectionListItem(
    int Id,int RentalId,int? VehicleId,string? VehicleName,string? LicensePlate,
    string InspectionType,bool? IsOk,string? InspectorEmail,DateTime? InspectedAt,
    decimal? OdometerKm,decimal? FuelBatteryLevel,string? DamageNotes,string? ImageUrls,
    string ConfirmationSource = "technician", string? ConfirmedBy = null);

public interface IInspectionDirectoryRepository
{
    Task<OperationsPage<InspectionListItem>> ListAsync(InspectionListQuery query,CancellationToken ct);
}
public interface IInspectionDirectoryService
{
    Task<OperationsPage<InspectionListItem>> ListAsync(InspectionListQuery query,CancellationToken ct);
}
public sealed class InspectionDirectoryService(IInspectionDirectoryRepository repository):IInspectionDirectoryService
{
    public Task<OperationsPage<InspectionListItem>> ListAsync(InspectionListQuery query,CancellationToken ct)
    {
        query.Validate();
        return repository.ListAsync(query,ct);
    }
}
