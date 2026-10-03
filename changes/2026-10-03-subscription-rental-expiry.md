# 2026-10-03 — Trừ hạn mức khi thuê bằng gói & job hết hạn subscription

## Đã làm

- **FR-024 — thuê bằng gói:** `CreateRentalAsync` giờ đọc `remaining_hours` của `customer_subscriptions` bằng `SELECT ... FOR UPDATE` trong cùng transaction:
  - Từ chối `InvalidOperationException("Subscription does not have enough remaining hours.")` khi `remaining_hours < duration` (đơn vị giờ).
  - Trừ `remaining_hours` ngay khi tạo rental; transaction rollback nên không trừ nếu rental thất bại.
  - Khóa `FOR UPDATE` chống double deduct khi hai request song song.
  - Gói có `remaining_hours IS NULL` (không giới hạn) được bỏ qua kiểm tra/trừ.
- **FR-025 — job hết hạn:** thêm `SubscriptionExpiryHostedService` (`BackgroundService`, `PeriodicTimer` 6 giờ):
  - `UPDATE customer_subscriptions SET status='expired' WHERE status='active' AND end_date < today` — idempotent.
  - Nhắc trước hạn 3 ngày qua Brevo (`IBrevoEmailSender.SendSubscriptionExpiryWarningAsync`), đánh dấu `reminder_sent_at` để không gửi lại; lỗi gửi 1 gói không chặn các gói khác.
  - Mốc thời gian UTC (`DateOnly.FromDateTime(DateTime.UtcNow)`), không phụ thuộc timezone server.
- **Sửa lỗi validate record:** `PlanRequest` và `RefundRequest` còn dùng `[property: ...]` khiến MVC trả 400 (đã chuyển sang `[param: ...]`, khớp phần còn lại của codebase). `JsonPropertyName` trên `ApiSuccessResponse` giữ nguyên vì là metadata serialize, không phải validate.
- **Schema:** `customer_subscriptions.reminder_sent_at` (migration `0009_subscription_expiry_reminder.sql`, đồng thời bổ sung vào `docs/db/Init.sql`).

## Test

- `SubscriptionRentalTests`: trừ đúng giờ, từ chối khi thiếu hạn mức (không trừ), hai rental song song chỉ một thành công.
- `SubscriptionExpiryTests`: job chuyển `expired` + chạy lần hai không đổi; danh sách nhắc đúng cửa sổ và biến mất sau khi đánh dấu.
- `UnitTests`: hai test validate record cập nhật theo cơ chế MVC dùng (attribute trên tham số primary constructor).
- `dotnet build -warnaserror`: xanh. `UnitTests`: 4/4 pass. Integration test cần PostgreSQL (`TEST_DATABASE_ADMIN`), chạy trong CI với service `postgres:16`.

## Chưa làm / bàn giao

- **BE-21 (FR-023) — thanh toán khi mua/gia hạn gói:** hiện `POST /api/subscription-plans/{id}/purchase` tạo `customer_subscriptions` ngay, chưa có bước thanh toán PayOS. Bảng `payments` đang `rental_id NOT NULL` và CHECK buộc `is_subscription_payment` phải `invoice_id IS NULL`, nên cần migration cho `rental_id` nullable + luồng checkout/webhook riêng. Chưa làm để tránh đổi phạm vi nghiệp vụ ngoài spec stub.
- Swagger mất `required`/min-max của `PlanRequest`/`RefundRequest` sau khi bỏ `property:` (trade-off đã ghi trong handoff).
