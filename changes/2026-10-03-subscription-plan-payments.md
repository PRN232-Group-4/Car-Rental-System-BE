# 2026-10-03 — Mua/gia hạn gói qua PayOS & endpoint "Gói của tôi"

- **FR/NFR:** FR-023 (mua/gia hạn gói), FR-024 (gói của tôi)
- **Task:** BE-21 (hoàn tất phần còn lại), hỗ trợ FE-09
- **Loại:** feat

## Đã làm

### 1. `GET /api/customer-subscriptions` (trước đây thiếu)

Chỉ có `POST /api/subscription-plans/{id}/purchase`; không có endpoint liệt kê gói của khách, nên FE-09 ("Gói của tôi") không chạy được.

- `OperationsContracts.cs`: thêm record `CustomerSubscription(int Id, int CustomerId, int PlanId, string PlanName, DateOnly StartDate, DateOnly EndDate, decimal? TotalHours, decimal? RemainingHours, string Status)` và `ListCustomerSubscriptionsAsync(string email, CancellationToken)`.
- `NpgsqlOperationsRepository.cs`: join `customer_subscriptions` → `customers`/`users`/`subscription_plans`, scope theo email, `totalHours` lấy từ `subscription_plans.included_hours`, sắp xếp `id DESC`.
- `OperationsController.cs`: `[Authorize(Customer)] GET customer-subscriptions`.
- `ApiSuccessResponseFilter.cs`: message `Operations.CustomerSubscriptions`.

### 2. Thanh toán PayOS khi mua gói (BE-21)

**Luồng:** `POST /api/subscription-plans/{planId}/purchase` → tạo `payments` `status='pending'` → gọi PayOS lấy `checkoutUrl` → khách thanh toán → PayOS gọi webhook → **webhook mới tạo `customer_subscriptions`** và đánh dấu `paid`. Subscription **không** được tạo trước khi thanh toán thành công.

- **Migration `0010_subscription_plan_payments.sql`:**
  - `payments.rental_id` → nullable (payment mua gói không gắn rental).
  - `payments.plan_id INTEGER` (NULL với payment rental/invoice).
  - `CREATE OR REPLACE FUNCTION fn_check_payment_matches_rental()`: bỏ qua kiểm tra khi `rental_id IS NULL` (trigger cũ luôn `SELECT is_subscription FROM rentals WHERE id = NEW.rental_id` nên chặn mọi payment không có rental).
  - Payment mua gói thoả `ck_payments_invoice_rule` sẵn có (`is_subscription_payment = TRUE AND invoice_id IS NULL`).
- **`PaymentContracts.cs`:**
  - `PlanCheckout(int PaymentId, long OrderCode, decimal Amount, string PlanName)`, `PlanPurchaseStatus(long OrderCode, string Status)`.
  - `SubscriptionOrderCodes`: PayOS `orderCode` là int64; gói dùng dải riêng `Base = 1_000_000_000 + paymentId` để webhook phân biệt với `invoiceId` (invoice checkout vẫn dùng `invoiceId` làm orderCode).
  - `PaymentSummary.RentalId` đổi thành `int?` (payment gói có `rental_id = NULL`).
  - Interface thêm `CreatePlanCheckoutAsync`, `CompletePlanPurchaseAsync`, `GetPlanPurchaseAsync`.
- **`NpgsqlPaymentRepository.cs`:**
  - `CreatePlanCheckoutAsync`: kiểm tra plan tồn tại + `is_active`, tạo payment `pending` (`type='subscription'`, `method='payos'`), trả `orderCode`.
  - `CompletePlanPurchaseAsync`: transaction + `FOR UPDATE`; idempotent khi `status='paid'` (webhook gửi lại không tạo gói trùng); chặn khi số tiền webhook khác giá gói; `INSERT customer_subscriptions` từ `duration_days`/`included_hours` của plan; cập nhật payment `paid` + `provider_reference`.
  - `GetPlanPurchaseAsync`: poll trạng thái, scope theo email chủ sở hữu.
  - `ReadPayment`: chịu được `rental_id` NULL.
- **`PaymentsController.cs`:**
  - `[Authorize(Customer)] POST subscription-plans/{planId:int}/purchase` → `CheckoutResult`; trả 503 khi PayOS chưa cấu hình.
  - `[Authorize(Customer)] GET subscription-plans/purchases/{orderCode:long}` → `PlanPurchaseStatus` (FE poll sau khi PayOS redirect về).
  - Webhook `payments/webhooks/payos`: rẽ nhánh `SubscriptionOrderCodes.IsSubscriptionOrder(orderCode)` → `CompletePlanPurchaseAsync`, còn lại → `CompleteInvoiceAsync` như cũ.
  - `SubscriptionReturnUrl`: gắn thêm `type=subscription` vào `PayOs__ReturnUrl` để FE biết poll đúng chỗ.
- **`OperationsController.cs`:** bỏ `POST subscription-plans/{id}/purchase` (mua tức thời, không thanh toán) — đã chuyển sang `PaymentsController`. `BuyPlanAsync` giữ lại cho test/seed.
- **`ApiSuccessResponseFilter.cs`:** bỏ message `Operations.Purchase`; thêm `Payments.PurchasePlan`, `Payments.PlanPurchase`.

## Chưa làm / bàn giao

- **Gia hạn cộng dồn:** mua lại cùng gói tạo `customer_subscriptions` mới (không cộng `remaining_hours` vào gói cũ), giống hành vi `BuyPlanAsync` hiện có.
- **Hoàn tiền cho gói:** luồng refund hiện chỉ áp cho payment theo invoice.
- **PayOS `ReturnUrl`/`CancelUrl`:** `appsettings.json` vẫn để placeholder `https://your-frontend.example/payments/success`; phải đổi thành `https://<fe-host>/payment/result` (và cancel tương ứng) trước khi chạy thật.
- **Endpoint public `GET /api/facilities`** vẫn `[Authorize(Roles="Admin")]` → landing/booking không tải được chi nhánh (thuộc catalog, ngoài phạm vi task này).

## Frontend liên quan (repo FE)

- `features/subscriptions/api.ts`: `purchaseSubscriptionPlan` trả `CheckoutResult`; thêm `getPlanPurchaseStatus`.
- `features/subscriptions/components/SubscriptionPlans.tsx`: redirect sang `checkoutUrl` bằng `location.assign`.
- `app/(dashboard)/payment/result/page.tsx`: xử lý cả hoá đơn và mua gói (poll `subscription-plans/purchases/{orderCode}`).
- `features/payments/types.ts`: `rentalId` nullable, thêm type `subscription`; `payment-history` hiển thị payment không có rental.
- Mock handler `subscription-plans/{id}/purchase` trả `checkoutUrl` + handler `subscription-plans/purchases/{orderCode}`.

## Test

- `SubscriptionPlanPaymentTests` (mới, 5 test): tạo payment `pending` không có rental; webhook hoàn tất → tạo subscription đúng `plan_id`/`remaining_hours`; gửi webhook lần hai không nhân đôi; sai số tiền → `InvalidOperationException`; plan inactive → `KeyNotFoundException`; scope theo chủ sở hữu.
- `SubscriptionPlanTests`: thêm 2 test cho `ListCustomerSubscriptionsAsync` (liệt kê đúng gói đã mua, scope theo chủ sở hữu).
- `dotnet build -warnaserror`: 0 warning / 0 error. `dotnet test`: **84/84 pass** (PostgreSQL thật).
- Smoke test HTTP (chạy WebApi local + Postgres/Redis container): ký HMAC thật gửi `POST /api/payments/webhooks/payos` → payment `paid`, tạo subscription 40 giờ; gửi lại → không nhân đôi; chữ ký sai → 400.

## Quyết định/giả định

- Payment mua gói dùng `is_subscription_payment = TRUE` vì `ck_payments_invoice_rule` bắt buộc `invoice_id IS NULL` khi `TRUE` (payment gói không có invoice). Phân biệt loại bằng `type='subscription'` và `plan_id IS NOT NULL`.
- Dùng offset `1_000_000_000` cho `orderCode` thay vì thêm cột `order_code`, để không đụng schema nhiều; `invoiceId` thực tế không thể chạm ngưỡng này.
- Subscription chỉ sinh sau khi webhook xác nhận, tránh gói "pending" hiển thị nhầm trong "Gói của tôi".
