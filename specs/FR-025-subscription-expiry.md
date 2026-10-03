# FR-025 — Theo dõi/hết hạn gói
- **Status:** Done; **Priority:** P1; **Milestone:** M6
Job đổi trạng thái expired, nhắc trước hạn. Acceptance: idempotent scheduled job, timezone documented.

## Triển khai 2026-10-03

- `SubscriptionExpiryHostedService` chạy mỗi 6 giờ và chạy ngay lần đầu khi khởi động (`BackgroundService` + `PeriodicTimer`).
- Mốc thời gian UTC: `DateOnly.FromDateTime(DateTime.UtcNow)` được truyền xuống DB, không phụ thuộc timezone server.
- Idempotent: `UPDATE ... WHERE status='active' AND end_date<$1`; email nhắc gated bởi cột `reminder_sent_at`.
- Nhắc trước 3 ngày qua Brevo (`SendSubscriptionExpiryWarningAsync`); lỗi gửi ở một gói được log và không chặn các gói khác.
- `customer_subscriptions` thêm cột `reminder_sent_at` (migration `0009`, đồng thời có trong `Init.sql`).
- Integration test: job chuyển `expired` và chạy lần hai không đổi gì; danh sách nhắc chỉ gồm gói active trong cửa sổ 3 ngày và biến mất sau khi đánh dấu.

## Thay đổi
- `changes/2026-10-03-subscription-rental-expiry.md`
