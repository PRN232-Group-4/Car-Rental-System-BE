# FR-015 — Pre-rental inspection
- **Status:** In progress; **Priority:** P0; **Milestone:** M4
Một pre-inspection/rental; `is_ok=true` mới renting. Acceptance: immutable evidence, state-machine test.

## Phân quyền cập nhật 2026-10-03

Phân quyền: policy Inspection cho Admin/Maintenance có staff profile. staffId trong body là tùy chọn; nếu gửi phải trùng claim staff_id, khác trả 403. Người thực hiện luôn lấy từ principal. CSKH/Driver không được tạo inspection. API đổi trạng thái không thể bỏ qua pre-inspection đạt.
