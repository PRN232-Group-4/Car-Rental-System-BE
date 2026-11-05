using Car_Rental_System_BE.Application.Features.Billing;

namespace Car_Rental_System_BE.Application.Interfaces.External;

public interface IPaymentGateway
{
    bool IsConfigured { get; }
    Task<CheckoutResult> CreateCheckoutAsync(CheckoutRequest request, CancellationToken ct);
    bool VerifyWebhook(PayOsWebhook webhook);

    /// <summary>
    /// Trạng thái link thanh toán theo orderCode (PENDING/PAID/CANCELLED/EXPIRED) hoặc null nếu
    /// không tra được. payOS không gửi webhook khi khách huỷ — chỉ redirect về cancelUrl — nên
    /// đây là cách duy nhất để biết link đã bị huỷ/hết hạn.
    /// </summary>
    Task<string?> GetPaymentStatusAsync(long orderCode, CancellationToken ct);
}
