using Car_Rental_System_BE.Application.DTOs.Operations;

namespace Car_Rental_System_BE.Application.Interfaces.Repositories;

public interface IOperationsRepository
{
    Task<Rental> ChangeRentalStatusAsync(int id, string status, CancellationToken ct);
    Task<IReadOnlyList<DriverScheduleEntry>> ListDriverScheduleAsync(int? driverStaffId, DateTime from, DateTime to, CancellationToken ct);
}
