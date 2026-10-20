# FR-026 — Bảo trì xe
- **Status:** Implemented for incident recovery; **Priority:** P1; **Milestone:** M7

Admin/Manager tạo maintenance hoặc giao Maintenance staff từ report. Report/rental/vehicle/maintenance liên kết trong transaction. Assignee phải active có role Maintenance; facility phải tồn tại.
Technician chỉ cập nhật công việc được giao, không đổi assignee. Admin/Manager quản lý mọi công việc. Status: open/scheduled/in_progress/completed/cancelled; completed yêu cầu isSafe=true. Unsafe/open maintenance chặn đặt xe; completed chỉ mở xe khi không còn bảo trì chưa xong/unsafe hoặc breakdown chưa xử lý.
Cost là chi phí nội bộ, không tự cộng invoice khách. Driver tạo report, Manager giao bảo trì. Xem FR-031.

2026-10-20: GET maintenance phân trang, lọc createdAt theo `[from,to)` UTC và trả `createdAt`/`rentalId`. Giao bảo trì từ report nhận thêm `maintenanceType`/`description` tùy chọn; mặc định giữ incident/mô tả report, validation tại HTTP/Application. Xem [FR-037](FR-038-operations-api-completion.md).
