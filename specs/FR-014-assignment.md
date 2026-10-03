# FR-014 — Phân xe/tài xế
- **Status:** In progress; **Priority:** P1; **Milestone:** M4

Admin và Manager xem lịch gán tài xế; Admin, Manager và CustomerService được gán tài xế cho rental. Xe gán là xe của rental, không nhận vehicle ID riêng.

| Method | Endpoint | Roles | Mô tả |
|---|---|---|---|
| GET | `/api/drivers/schedule?from={UTC}&to={UTC}&driverStaffId={optional}` | Admin, Manager; Driver chỉ lịch của mình | Lịch assignment còn hiệu lực, lọc theo khoảng thời gian và tùy chọn theo tài xế. |
| POST | `/api/rentals/{id}/assignments` | Admin, Manager, CustomerService | Tạo hoặc đổi assignment hiện hành; body: `{ "driverStaffId": 123 }`. |

Acceptance: driver phải active, `is_driver=true`, bằng lái còn hạn và không trùng lịch; rental phải có pre-inspection đạt. Mỗi rental chỉ có một assignment `assigned`; thay tài xế tạo record mới và chuyển record trước đó thành `reassigned`. Email người thực hiện được lưu tại `vehicle_assignments.assigned_by`.

Driver bỏ driverStaffId sẽ được server lọc theo staff_id; gửi ID tài xế khác trả 403.
