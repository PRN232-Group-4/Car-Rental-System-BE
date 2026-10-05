# Câu hỏi mở và giả định

| 2026-10-06 | FR-018 | Gỡ bỏ auto-refund tại AddInspectionAsync: hoàn cọc chỉ xử lý duy nhất tại complete sau khi tính toán quá giờ để tránh hoàn cọc 2 lần hoặc thất thoát. | Đã thống nhất với Huy (O2), đã kiểm chứng qua test. |
| 2026-10-06 | FR-018 | Chính sách hủy đơn: khách hủy trước 24h hoàn 100%; dưới 24h giữ phí phạt 20% tiền thuê, hoàn 80% tiền thuê + 100% cọc; Staff/System hủy hoàn 100%. | Đã thống nhất (O1), xem ADR-004. |
| 2026-10-06 | FR-018 | Hóa đơn thiếu hụt quá giờ: tạo với kind = 'overtime', hạn thanh toán 72h; partial unique index chặn trùng lặp. | Xác nhận với Phúc (BE-18/19) cách xử lý thanh toán hóa đơn này (O3). |
| 2026-10-06 | FR-018 | Thuê bằng gói thuê bao khi trả trễ: trừ giờ trễ vào remaining_hours; không thu/hoàn cọc tiền mặt. | Đã triển khai và tích hợp với ISubscriptionUsage (O4). |
| 2026-10-06 | FR-018 | Xe hỏng khi nhận lại (is_ok = false): đánh dấu damage_review_required = true trên rental_settlements để Staff lập biên bản sự cố riêng sau. | Đã thống nhất với Huy (O4), xem ADR-004. |
| 2026-10-04 | FR-014 | Đổi xe khác loại: hiện tại chặn (409 VEHICLE_TYPE_MISMATCH) để tránh phá vỡ tính toán giá ban đầu. | Cần Lạc (BE-12/33) xác nhận nếu muốn cho phép đổi chéo loại xe kèm tính bù trừ chênh lệch (O2). |
| 2026-10-04 | FR-014 | Kiểm tra hạng GPLX theo loại xe theo luật mới: tạm tắt qua config (Assignment:CheckLicenseClass=false). | Chờ nhóm nghiệp vụ cung cấp ma trận phân hạng GPLX tương thích (O3). |
| 2026-10-04 | FR-014 | Kiểm định xe thay thế: bảng inspections hiện có UNIQUE(rental_id, inspection_type) nên xe thay thế chưa thể ghi thêm pre-inspection. | Đề xuất cho BE-15/16 (Lạc/Anh) bổ sung cột assignment_id vào bảng inspections (O4). |
| 2026-10-04 | FR-014 | Phụ thuộc vòng giữa BE-33 và BE-14: giải quyết bằng cổng IDriverAvailability mặc định trong Application. | Cần Lạc (BE-33) xác nhận triển khai adapter ca làm việc/ngày nghỉ qua cổng này (O5). |
| 2026-10-04 | FR-010 | Quy tắc chặn của bảo dưỡng: coi các trạng thái 'open', 'scheduled', 'in_progress', hoặc 'completed' mà chưa 'is_safe' là đang chặn xe. | Xác nhận với Kiệt (BE-24) các giá trị status thật của bảng vehicle_maintenance (hiện code dùng 'open'). |
| 2026-10-04 | FR-010 | Đệm 60 phút đối xứng ở cả 2 đầu: không cho phép đặt xe bắt đầu ngay trong thời gian đệm của lượt thuê trước. | Xác nhận với Phúc/Lạc xem khách có được nhận xe sớm trong đệm nếu xe đã vệ sinh xong không. |
| 2026-10-04 | FR-010 | Bảng vehicles hiện chưa có cột license_plate; FE mock đang hiển thị biển số. Giữ nguyên schema, không tự ý thêm cột. | Xác nhận với nhóm xem có cần migration thêm license_plate vào bảng vehicles không. |
| 2026-10-04 | FR-010 | Đệm mặc định lưu theo dòng (buffer_minutes = 60). Thay đổi đệm mặc định sau này đòi hỏi migration tính lại cột period. | Xem chi tiết trong ADR-002. |
| 2026-10-03 | FR-007/008/026 | Department là tổ chức; staffRole cấp quyền. Một specialty cho mỗi staff trong API quản trị; Maintenance = Technician. Không tự cấp CustomerService cho Staff cũ. | Có cần kiêm nhiệm nhiều specialty qua API sau này? |
| 2026-10-03 | FR-007/026 | Maintenance làm việc toàn đội xe; facility và maintenance assignment chưa giới hạn truy cập. | Nếu phân tách cơ sở, bổ sung resource policy riêng. |
| 2026-09-30 | FR-001 | Hệ thống vận hành một đội xe, nên tạo `customers` cùng user đã verify. | Seed/role Customer có cần ngay khi RBAC làm? |
| 2026-09-30 | FR-001/003 | Redis URL dùng dạng StackExchange.Redis; `REDIS_URL` URI Railway có thể cần adapter. | Kiểm tra connection format Railway khi tạo project. |
| 2026-09-30 | FR-002 | Rate-limit edge chưa có vì chưa xác định reverse proxy. | Chọn ASP.NET rate limiter/Redis distributed limiter. |
