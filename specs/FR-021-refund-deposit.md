# FR-021 — Hoàn tiền/cọc
- **Status:** In progress
- **Priority:** P1
- **Milestone:** M5

## Mô tả
Hoàn tiền/cọc được ghi thành payment `type='refund'` trong ledger, trạng thái `requested → paid | rejected`. Inspection không tự tạo refund; Admin/Manager/CustomerService gửi yêu cầu hoàn qua API payment riêng sau khi kiểm tra hồ sơ. Admin duyệt/từ chối; `reviewed_by`/`reviewed_at` lưu vết audit.

## API
| Method | Path | Auth | Mô tả |
|---|---|---|---|
| POST | `/api/payments/{paymentId}/refunds` | Admin, Manager, CustomerService | Tạo yêu cầu hoàn tiền/cọc |
| POST | `/api/payments/refunds/{refundId}/approve` | Admin | Duyệt hoàn tiền (`paid`) |
| POST | `/api/payments/refunds/{refundId}/reject` | Admin | Từ chối yêu cầu |

## Acceptance criteria
- [x] Không hoàn quá số đã thu (`refunded + amount <= paid`, chỉ tính `requested`/`paid`)
- [x] Approval + audit ledger (`payments` + `reviewed_by`/`reviewed_at`)
- [x] Inspection không tự tạo giao dịch tài chính; refund được yêu cầu riêng
- [x] Chỉ refund `requested` mới được duyệt/từ chối
- [x] Integration test (no over-refund, approval/audit, inspection không tạo refund)
- [ ] Payout PayOS thực tế khi có payout key

## Thay đổi
- `changes/2026-10-01-invoice-payment-subscription.md`
