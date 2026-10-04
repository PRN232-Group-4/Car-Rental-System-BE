# FR-014 — Phân xe/tài xế
- **Status:** Implemented for O2/O5/BE-24; **Priority:** P1; **Milestone:** M4

Admin/Manager tạo/sửa/hủy ca qua POST/PUT /api/drivers/shifts; GET trả ca trực độc lập. GET /api/drivers/schedule trả lịch chuyến. Driver chỉ xem lịch của mình.
POST /api/rentals/{id}/assignments dành cho Admin/Manager sau pre-inspection đạt hoặc khi renting. Driver phải active, bằng còn hạn đến hết chuyến, trong ca, không trùng chuyến. Không sửa/hủy ca làm mất coverage. CustomerService không phân công.
POST /api/rentals/{id}/replacements: cùng loại, đúng số chỗ, khả dụng và có pre-inspection riêng. Giữ snapshot giá, invoice, cọc. Khi renting: bắt buộc breakdown report đã giao Maintenance và tài xế mới khác hiện tại.
Xem [FR-031](FR-031-operational-recovery.md), [API frontend](../docs/operational-recovery.md).
