# FR-024 — Thuê bằng gói
- **Status:** In progress; **Priority:** P0; **Milestone:** M6
Rental subscription không tạo invoice, trừ remaining hours. Acceptance: đủ hạn mức, transaction chống double deduct.

## Phân quyền cập nhật 2026-10-03

Phân quyền: Customer chỉ đặt bằng subscription của mình, status active và còn trong thời hạn. Subscription không thuộc khách trả 404 trước khi tạo rental. Kiểm tra ownership có integration test; trừ hạn mức và chống double deduct vẫn thuộc phần chưa hoàn thành.
