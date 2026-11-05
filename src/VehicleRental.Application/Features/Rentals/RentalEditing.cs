using System.ComponentModel.DataAnnotations;
using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.DTOs.Operations;

namespace Car_Rental_System_BE.Application.Features.Rentals;

public sealed record UpdateRentalRequest(
    [Required, Range(1, int.MaxValue)] int? VehicleId,
    [Required] DateTime? StartTime,
    [Required] DateTime? EndTime,
    [Required] bool? HasDriver);

public sealed record UpdateRentalCommand(int VehicleId, DateTime StartTime, DateTime EndTime, bool HasDriver);

public interface IRentalEditingService
{
    Task<Rental> UpdateAsync(int id, UpdateRentalRequest request, CancellationToken ct);
}

public interface IRentalEditingRepository
{
    Task<Rental> UpdateBeforePickupAsync(int id, UpdateRentalCommand command, CancellationToken ct);
}

public sealed class RentalEditingService(IRentalEditingRepository repository) : IRentalEditingService
{
    public Task<Rental> UpdateAsync(int id, UpdateRentalRequest request, CancellationToken ct)
    {
        if (id < 1 || request.VehicleId is null or < 1 || request.StartTime is null || request.EndTime is null || request.HasDriver is null)
            throw new BusinessRuleException("INVALID_RENTAL_EDIT", "vehicleId, startTime, endTime and hasDriver are required.");
        var start = request.StartTime.Value;
        var end = request.EndTime.Value;
        if (start == default || end <= start || start.Kind != DateTimeKind.Utc || end.Kind != DateTimeKind.Utc)
            throw new BusinessRuleException("INVALID_RENTAL_EDIT", "Provide UTC startTime/endTime with endTime after startTime.");
        return repository.UpdateBeforePickupAsync(id, new(request.VehicleId.Value, start, end, request.HasDriver.Value), ct);
    }
}

public sealed record RentalEditingPrice(decimal Total, decimal Vat, decimal Deposit, decimal Unit);

public static class RentalEditingRules
{
    public static void RequireEditable(string status, DateTime? startedAt)
    {
        if (status is not ("pending" or "pre_inspection") || startedAt is not null)
            throw new ConflictException("RENTAL_NOT_EDITABLE", "Only rentals that have not started can be edited.");
    }

    public static RentalEditingPrice Reprice(Rental rental, UpdateRentalCommand command, decimal unit)
    {
        if (rental.StartTime is not { } oldStart || rental.EndTime is not { } oldEnd || oldEnd <= oldStart || unit <= 0)
            throw new ConflictException("RENTAL_PRICING_UNAVAILABLE", "The rental has no valid price/schedule snapshot.");
        var oldBase = rental.TotalPrice - rental.VatPrice;
        if (oldBase <= 0 || rental.VatPrice < 0 || rental.DepositPrice < 0 || rental.OvertimePrice != 0)
            throw new ConflictException("RENTAL_PRICING_UNAVAILABLE", "The rental monetary snapshot cannot be safely repriced.");
        try
        {
            var hours = (decimal)(command.EndTime - command.StartTime).TotalHours;
            var basis = unit * (rental.RentalType == "hourly" ? hours : Math.Max(1, Math.Ceiling(hours / 720m)));
            var vat = Round(basis * rental.VatPrice / oldBase);
            var deposit = Round(basis * rental.DepositPrice / oldBase);
            var total = Round(basis) + vat;
            if (total + deposit > 999999999999.99m)
                throw new OverflowException();
            return new(total, vat, deposit, unit);
        }
        catch (OverflowException)
        {
            throw new BusinessRuleException("INVALID_RENTAL_EDIT", "The requested rental amount exceeds the supported range.");
        }
    }

    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
