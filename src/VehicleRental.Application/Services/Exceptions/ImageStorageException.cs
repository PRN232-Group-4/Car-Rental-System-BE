namespace Car_Rental_System_BE.Application.Services.Exceptions;

/// <summary>Lỗi tải lên do hạ tầng, không bao giờ là lỗi kiểm tra dữ liệu khách nhập.</summary>
public sealed class ImageStorageException(string message) : Exception(message);
