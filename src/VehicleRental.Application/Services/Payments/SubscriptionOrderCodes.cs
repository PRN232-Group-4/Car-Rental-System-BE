

namespace Car_Rental_System_BE.Application.Services.Payments;

public static class SubscriptionOrderCodes
{
    public const long Base = 1_000_000_000L;
    public static long ForPayment(int paymentId) => Base + paymentId;
    public static bool IsSubscriptionOrder(long orderCode) => orderCode >= Base;
    public static int ToPaymentId(long orderCode) => (int)(orderCode - Base);
}
