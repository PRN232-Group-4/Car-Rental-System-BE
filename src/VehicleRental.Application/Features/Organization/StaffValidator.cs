namespace Car_Rental_System_BE.Application.Features.Organization;

public static class StaffValidator
{
    public static void ValidateDriver(bool isDriver, string? number, DateOnly? expiry, DateOnly? issue)
    {
        if (!isDriver) return;
        if (string.IsNullOrWhiteSpace(number)) throw new StaffLicenseException("Tài xế phải có số giấy phép lái xe.");
        if (!expiry.HasValue) throw new StaffLicenseException("Tài xế phải có ngày hết hạn giấy phép.");
        if (issue.HasValue && issue > expiry) throw new StaffLicenseException("Ngày cấp giấy phép phải trước hoặc bằng ngày hết hạn.");
    }
}
