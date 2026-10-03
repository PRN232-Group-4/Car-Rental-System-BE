# FR-020 — Thanh toán hóa đơn
- **Status:** In progress
- **Priority:** P1
- **Milestone:** M5

## Mô tả
Payment của direct rental bắt buộc gắn `invoice_id` (`ck_payments_invoice_rule`). Checkout qua PayOS; webhook xác thực chữ ký và idempotent theo `provider_reference`. Khi PayOS chưa cấu hình, checkout trả 503 và không tạo payment giả.

## API
| Method | Path | Auth | Mô tả |
|---|---|---|---|
| POST | `/api/invoices/{invoiceId}/checkout` | Customer (chủ hóa đơn) | Tạo link thanh toán PayOS |
| POST | `/api/payments/webhooks/payos` | Anonymous (signature-verified) | Nhận webhook, đánh dấu invoice `paid` |
| GET | `/api/payments` | Customer (của mình), Admin/Manager/CustomerService (tất cả) | Danh sách payment, lọc `invoiceId` |

## Acceptance criteria
- [x] Payment direct rental bắt buộc `invoice_id`; subscription payment không có invoice
- [x] Webhook idempotent theo `provider_reference` (unique partial index)
- [x] Webhook sai chữ ký trả 400; lệch amount bị từ chối
- [x] Invoice chuyển `paid` sau webhook hợp lệ
- [x] Trả 503 khi PayOS chưa cấu hình
- [x] Integration test (idempotent webhook, invoice chuyển `paid`)
- [ ] Payout worker chủ động gửi tiền hoàn (hiện chỉ ghi ledger)

## Thay đổi
- `changes/2026-10-01-payos-payment-scaffold.md`
- `changes/2026-10-01-invoice-payment-subscription.md`
