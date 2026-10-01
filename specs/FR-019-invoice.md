# FR-019 — Hóa đơn direct rental
- **Status:** In progress
- **Priority:** P1
- **Milestone:** M5

## Mô tả
Chỉ direct rental (`rentals.is_subscription = false`) mới có hóa đơn. Invoice được tạo tự động khi tạo rental và có thể tạo lại/idempotent qua API cho Admin/Staff. Rental theo gói bị chặn ở cả Application (lỗi 400) và DB trigger `trg_invoice_no_subscription`.

`invoice.amount = rentals.total_price + rentals.deposit_price` (tiền thuê + VAT + cọc), `vat_amount = rentals.vat_price`.

## API
| Method | Path | Auth | Mô tả |
|---|---|---|---|
| GET | `/api/invoices` | Customer (của mình), Admin/Staff (tất cả) | Danh sách hóa đơn |
| GET | `/api/invoices/{id}` | Customer (của mình), Admin/Staff | Chi tiết hóa đơn |
| POST | `/api/rentals/{id}/invoice` | Admin, Staff | Tạo hóa đơn cho direct rental (idempotent: trả invoice đã có nếu đã tồn tại) |

## Acceptance criteria
- [x] Invoice tự tạo khi tạo direct rental
- [x] Trigger `fn_block_invoice_for_subscription` / `trg_invoice_no_subscription` chặn invoice cho subscription
- [x] `invoice_number` unique (`ux_invoices_number`), định dạng `INV-yyyyMMdd-{rentalId}`
- [x] Application trả 400 trước khi chạm trigger khi rental là subscription
- [x] Invoice amount bao gồm cọc
- [ ] Integration test khi chạm hạ tầng

## Thay đổi
- `changes/2026-10-01-invoice-payment-subscription.md`
