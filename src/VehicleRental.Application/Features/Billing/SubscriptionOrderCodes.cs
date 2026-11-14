namespace Car_Rental_System_BE.Application.Features.Billing;

/// <summary>
/// SUBSCRIPTIONS (module dự kiến tách sau này). Quy ước mã đơn dùng chung cho luồng mua gói thuê bao
/// và webhook của nhà cung cấp: mã đơn lớn hơn <see cref="Base"/> là mua gói, nhỏ hơn là hóa đơn. Tạm
/// nằm trong Billing; giai đoạn sau sẽ tách ra cùng các thành viên mua gói của <see
/// cref="IPaymentService"/> và bộ điều phối webhook.
/// </summary>
public static class SubscriptionOrderCodes
{
    public const long Base = 1_000_000_000L;
    public static long ForPayment(int paymentId) => Base + paymentId;
    public static bool IsSubscriptionOrder(long orderCode) => orderCode >= Base;
    public static int ToPaymentId(long orderCode) => (int)(orderCode - Base);
}
