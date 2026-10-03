# FR-021 — Hoàn tiền/cọc
- **Status:** In progress
- **Priority:** P1
- **Milestone:** M5

## Mô tả
Hoàn tiền/cọc được ghi thành payment `type='refund'` trong ledger, trạng thái `requested → paid | rejected`. Sau post-inspection đạt của direct rental (invoice đã `paid`, có cọc), hệ thống tự tạo yêu cầu hoàn cọc (`method='deposit'`, idempotent). Admin duyệt/từ chối; `reviewed_by`/`reviewed_at` lưu vết audit.

## API
| Method | Path | Auth | Mô tả |
|---|---|---|---|
| POST | `/api/payments/{paymentId}/refunds` | Admin, Manager, CustomerService | Tạo yêu cầu hoàn tiền/cọc |
| POST | `/api/payments/refunds/{refundId}/approve` | Admin | Duyệt hoàn tiền (`paid`) |
| POST | `/api/payments/refunds/{refundId}/reject` | Admin | Từ chối yêu cầu |

## Acceptance criteria
- [x] Không hoàn quá số đã thu (`refunded + amount <= paid`, chỉ tính `requested`/`paid`)
- [x] Approval + audit ledger (`payments` + `reviewed_by`/`reviewed_at`)
- [x] Hoàn cọc tự động sau post-inspection đạt, chỉ khi invoice đã thanh toán
- [x] Chỉ refund `requested` mới được duyệt/từ chối
- [x] Integration test (no over-refund, approval/audit, hoàn cọc sau post-inspection)
- [ ] Payout PayOS thực tế khi có payout key

## Thay đổi
- `changes/2026-10-01-invoice-payment-subscription.md`
