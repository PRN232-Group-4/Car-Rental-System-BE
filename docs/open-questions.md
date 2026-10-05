# Câu hỏi mở và giả định

| Ngày | Liên quan | Giả định đã chọn | Cần xác nhận |
|---|---|---|---|
| 2026-10-03 | FR-007/008/026 | Department là tổ chức; staffRole cấp quyền. Một specialty cho mỗi staff trong API quản trị; Maintenance = Technician. Không tự cấp CustomerService cho Staff cũ. | Có cần kiêm nhiệm nhiều specialty qua API sau này? |
| 2026-10-03 | FR-007/026 | Maintenance làm việc toàn đội xe; facility và maintenance assignment chưa giới hạn truy cập. | Nếu phân tách cơ sở, bổ sung resource policy riêng. |
| 2026-09-30 | FR-001 | Hệ thống vận hành một đội xe, nên tạo `customers` cùng user đã verify. | Seed/role Customer có cần ngay khi RBAC làm? |
| 2026-09-30 | FR-001/003 | Redis URL dùng dạng StackExchange.Redis; `REDIS_URL` URI Railway có thể cần adapter. | Kiểm tra connection format Railway khi tạo project. |
| 2026-09-30 | FR-002 | Rate-limit edge chưa có vì chưa xác định reverse proxy. | Chọn ASP.NET rate limiter/Redis distributed limiter. |
| 2026-10-04 | O2/O4/O5/BE-24 | Inspection kiểm tra an toàn, không tự tính phí/hoàn cọc; staff report riêng. Ca trực độc lập bao phủ chuyến. Đổi xe giữ giá, đúng số chỗ; từ chối nếu thiếu seat_count. | Cụm "việc ko trả phí" hiểu là inspection không xử lý phí; công nợ/bồi thường riêng chưa nằm trong thay đổi này. |

## Department assignment — 2026-10-05 assumptions
Assignment moves an existing Staff/Manager account to a department and does not grant the Manager role. Manager assignment authority is system-wide, matching existing management policies. Multiple Managers may belong to one department; no single department-head relationship is introduced.
