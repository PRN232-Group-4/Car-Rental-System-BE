using Car_Rental_System_BE.Application.DTOs.Operations;

namespace Car_Rental_System_BE.Application.Features.Rentals;

/// <summary>
/// Đặt xe và đọc đơn thuê: tạo yêu cầu kèm ảnh chụp giá và hạn thanh toán, đọc đơn thuê phẳng theo id, và cùng
/// phép đọc đó nhưng thu hẹp theo những gì bên gọi được phép xem. Các thành viên này từng nằm trên
/// <c>IOperationsRepository</c>/<c>IOperationsService</c> cùng phần còn lại của nghiệp vụ vận hành.
/// </summary>
public interface IRentalBookingRepository
{
    Task<Rental> CreateRentalAsync(string email, int vehicleId, int? policyId, string rentalType, DateTime start, DateTime end, bool subscription, int? subscriptionId, bool hasDriver, CancellationToken ct, string paymentMethod = "payos", DateTime? expireAt = null);
    Task<Rental?> GetRentalAsync(int id, CancellationToken ct);
    Task<Rental?> GetRentalForUserAsync(int id, string email, bool readAll, bool customer, bool driver, CancellationToken ct);
}

public interface IRentalBookingService
{
    Task<Rental> CreateRentalAsync(string email, int vehicleId, int? policyId, string rentalType, DateTime start, DateTime end, bool subscription, int? subscriptionId, bool hasDriver, CancellationToken ct, string paymentMethod = "payos");
    Task<Rental?> GetRentalAsync(int id, CancellationToken ct);
    Task<Rental?> GetRentalForUserAsync(int id, string email, bool readAll, bool customer, bool driver, CancellationToken ct);
}
