# Câu hỏi mở và giả định

| Ngày | Liên quan | Giả định đã chọn | Cần xác nhận |
| 2026-10-04 | FR-010 | Quy tắc chặn của bảo dưỡng: coi các trạng thái 'open', 'scheduled', 'in_progress', hoặc 'completed' mà chưa 'is_safe' là đang chặn xe. | Xác nhận với Kiệt (BE-24) các giá trị status thật của bảng vehicle_maintenance (hiện code dùng 'open'). |
| 2026-10-04 | FR-010 | Đệm 60 phút đối xứng ở cả 2 đầu: không cho phép đặt xe bắt đầu ngay trong thời gian đệm của lượt thuê trước. | Xác nhận với Phúc/Lạc xem khách có được nhận xe sớm trong đệm nếu xe đã vệ sinh xong không. |
| 2026-10-04 | FR-010 | Bảng vehicles hiện chưa có cột license_plate; FE mock đang hiển thị biển số. Giữ nguyên schema, không tự ý thêm cột. | Xác nhận với nhóm xem có cần migration thêm license_plate vào bảng vehicles không. |
| 2026-10-04 | FR-010 | Đệm mặc định lưu theo dòng (buffer_minutes = 60). Thay đổi đệm mặc định sau này đòi hỏi migration tính lại cột period. | Xem chi tiết trong ADR-002. |
| 2026-10-03 | FR-007/008/026 | Department là tổ chức; staffRole cấp quyền. Một specialty cho mỗi staff trong API quản trị; Maintenance = Technician. Không tự cấp CustomerService cho Staff cũ. | Có cần kiêm nhiệm nhiều specialty qua API sau này? |
| 2026-10-03 | FR-007/026 | Maintenance làm việc toàn đội xe; facility và maintenance assignment chưa giới hạn truy cập. | Nếu phân tách cơ sở, bổ sung resource policy riêng. |
| 2026-09-30 | FR-001 | Hệ thống vận hành một đội xe, nên tạo `customers` cùng user đã verify. | Seed/role Customer có cần ngay khi RBAC làm? |
| 2026-09-30 | FR-001/003 | Redis URL dùng dạng StackExchange.Redis; `REDIS_URL` URI Railway có thể cần adapter. | Kiểm tra connection format Railway khi tạo project. |
| 2026-09-30 | FR-002 | Rate-limit edge chưa có vì chưa xác định reverse proxy. | Chọn ASP.NET rate limiter/Redis distributed limiter. |
