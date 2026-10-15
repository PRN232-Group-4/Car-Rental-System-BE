using System.ComponentModel.DataAnnotations;
using Car_Rental_System_BE.Application.DTOs.Operations;

namespace Car_Rental_System_BE.Application.Validators;

public static class RentalPolicyValidator
{
    public static ValidationException InvalidVehicleType() => Error("VehicleTypeId", "Loại phương tiện không tồn tại hoặc đã bị xóa. Vui lòng chọn lại.");
    public static ValidationException ActivePolicyExists() => Error("VehicleTypeId", "Phạm vi loại xe này đã có chính sách đang hoạt động. Vui lòng tạm ngưng chính sách cũ trước khi thêm.");

    public static void Validate(RentalPolicy policy)
    {
        if (policy.VehicleTypeId is <= 0) throw InvalidVehicleType();
        if (string.IsNullOrWhiteSpace(policy.Name)) throw Error("Name", "Vui lòng nhập tên chính sách.");
        foreach (var (field, value) in new[] { ("VatRate", policy.VatRate), ("OvertimeFeeRate", policy.OvertimeFeeRate), ("DepositPercentage", policy.DepositPercentage) })
            if (value is < 0 or > 100) throw Error(field, "Tỷ lệ phải từ 0 đến 100%.");
    }

    public static void ValidatePagination(int page, int pageSize)
    {
        if (page < 1) throw new ArgumentOutOfRangeException(nameof(page), "Page must be at least 1.");
        if (pageSize is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(pageSize), "Page size must be between 1 and 100.");
    }

    private static ValidationException Error(string field, string message) => new(new ValidationResult(message, [field]), null, null);
}
