# FR-023 — Mua/gia hạn gói
- **Status:** Done; **Priority:** P1; **Milestone:** M6
Tạo customer subscription và thanh toán. Acceptance: date range/hours correct, callback idempotent.

## API
| Method | Path | Auth | Mô tả |
|---|---|---|---|
| POST | `/api/subscription-plans/{planId}/purchase` | Customer | Tạo payment `pending` và trả PayOS `checkoutUrl` |
| POST | `/api/payments/webhooks/payos` | Anonymous (chữ ký PayOS) | Xác nhận thanh toán → tạo `customer_subscriptions` |
| GET | `/api/subscription-plans/purchases/{orderCode}` | Customer | Trạng thái giao dịch mua gói (FE poll sau khi PayOS redirect) |
| GET | `/api/customer-subscriptions` | Customer | Danh sách gói thuê bao của khách |

## Acceptance criteria
- [x] Khoảng ngày (`start_date`/`end_date`) và số giờ (`remaining_hours`) đúng theo plan
- [x] Callback (webhook) idempotent khi PayOS gửi lại
- [x] Integration test khi chạm hạ tầng (PostgreSQL)

## Hoàn thành 2026-10-03

- [x] `POST .../purchase` tạo payment `pending` (`type='subscription'`, `plan_id`, `rental_id NULL`, `is_subscription_payment = TRUE`) rồi gọi PayOS; **không** tạo subscription trước khi thanh toán xong
- [x] `orderCode` mua gói ở dải riêng (`1_000_000_000 + paymentId`) để webhook phân biệt với `invoiceId`
- [x] Webhook verify chữ ký + số tiền, tạo `customer_subscriptions` từ `duration_days`/`included_hours`, đánh dấu payment `paid` — gửi lại không tạo gói trùng
- [x] Sai số tiền → `InvalidOperationException` (rollback, không tạo gói); plan inactive → `KeyNotFoundException`
- [x] `GET /api/customer-subscriptions` scope theo email của khách (trả 401 khi chưa đăng nhập)
- [x] Migration `0010_subscription_plan_payments.sql`: `payments.rental_id` nullable, thêm `payments.plan_id`, trigger `fn_check_payment_matches_rental` bỏ qua payment không có rental
- [x] Integration test: checkout pending không tạo gói, hoàn tất + idempotent, sai số tiền, plan inactive, scope theo chủ sở hữu
- [x] Smoke test HTTP: ký HMAC thật gửi webhook → tạo gói; gửi lại không nhân đôi; chữ ký sai → 400

## Chưa làm
- Gia hạn cộng dồn: mua lại cùng gói tạo `customer_subscriptions` mới, không cộng vào gói cũ
- Hoàn tiền cho payment mua gói (refund hiện chỉ áp cho payment theo invoice)
- Cần cấu hình `PayOs__ReturnUrl`/`PayOs__CancelUrl` trỏ về trang kết quả của FE

## Thay đổi
- `changes/2026-10-03-subscription-plan-payments.md`
