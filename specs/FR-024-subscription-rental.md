# FR-024 — Thuê bằng gói
- **Status:** Done; **Priority:** P0; **Milestone:** M6
Rental subscription không tạo invoice, trừ remaining hours. Acceptance: đủ hạn mức, transaction chống double deduct.

## Phân quyền cập nhật 2026-10-03

Phân quyền: Customer chỉ đặt bằng subscription của mình, status active và còn trong thời hạn. Subscription không thuộc khách trả 404 trước khi tạo rental.

## Hoàn thành 2026-10-03

- [x] Kiểm tra ownership + trạng thái active/còn hạn (integration test)
- [x] Trừ `remaining_hours` trong cùng transaction khi tạo rental, khóa `FOR UPDATE` chống double deduct
- [x] Từ chối (`InvalidOperationException`) khi gói không đủ hạn mức; rollback không trừ giờ
- [x] Integration test: trừ đúng số giờ, từ chối khi thiếu, hai rental song song chỉ một thành công

## Thay đổi
- `changes/2026-10-03-subscription-rental-expiry.md`
