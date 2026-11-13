namespace Car_Rental_System_BE.Application.Features.Billing;

/// <summary>
/// SUBSCRIPTIONS (intended future module). The order-code convention shared by the subscription-plan
/// purchase flow and the provider webhook: an order code above <see cref="Base"/> is a plan purchase,
/// anything below it is an invoice. It lives in Billing for now; the next phase moves it out together
/// with the plan-purchase members of <see cref="IPaymentService"/> and the webhook dispatcher.
/// </summary>
public static class SubscriptionOrderCodes
{
    public const long Base = 1_000_000_000L;
    public static long ForPayment(int paymentId) => Base + paymentId;
    public static bool IsSubscriptionOrder(long orderCode) => orderCode >= Base;
    public static int ToPaymentId(long orderCode) => (int)(orderCode - Base);
}
