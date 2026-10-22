# SQL migrations

`0028_driver_inspection_confirmation.sql` lưu has_driver cho dài hạn; phân biệt nguồn kiểm tra technician/driver_default/admin_confirmation và người xác nhận. Trigger tự xác nhận trước thuê cho đơn có tài xế khi tạo/đổi xe; backfill chỉ đơn pending/pre_inspection chưa bắt đầu, không ghi đè evidence đã có. Hợp đồng dài hạn cũ giữ tự lái. Runner áp dụng tự động khi backend khởi động; triển khai đồng bộ với FE.

`0026_audit_log_metadata.sql` bổ sung IP nullable, status mặc định success và index thứ tự thời gian cho nhật ký quản trị tài khoản; dữ liệu cũ không tự có IP.

`0027_role_catalogue.sql` thêm unique index `lower(name)` cho role. Nếu dữ liệu cũ trùng tên không phân biệt hoa thường, migration dừng để xử lý rõ ràng; không tự gộp role hoặc xóa membership.

`0025_inspection_assignments.sql` lưu trách nhiệm kiểm tra trước thuê theo rental/vehicle, kỹ thuật viên Maintenance và người/thời gian phân công. Migration runner tự phát hiện; không backfill hoặc thay đổi evidence cũ.

`0020_invoice_checkout_reservation.sql` thêm marker checkout_started_at trước khi phát hành link PayOS để khóa sửa điều khoản rental. Lần nâng cấp đầu backfill invoice unpaid cũ (không có lịch sử phát hành link), giữ marker khi provider lỗi; replay không đánh dấu invoice mới. Xem ADR-007 và change 2026-10-16.

`0019_rental_policy_usage.sql` bổ sung index `rentals(policy_id)` cho số lần thuê theo policy. Runner hiện tự tìm các file `*.sql` theo tên; không cần đăng ký thêm trong mã. Migration chỉ thêm index, không chỉnh dữ liệu hay tự tắt policy active trùng có sẵn.

`0001_initial_schema` là `docs/db/Init.sql`, được migration runner áp dụng và ghi checksum vào `schema_migrations`. Một số database legacy đã áp dụng baseline trước khi `Init.sql` bị cập nhật; runner sẽ giữ checksum cũ của riêng `0001` và tiếp tục áp dụng migration `0002+`. Checksum được chuẩn hóa line ending để cùng SQL chạy trên Windows/Linux không bị khác hash; checksum nội dung migration vẫn được kiểm tra nghiêm ngặt.

Mỗi thay đổi schema tiếp theo tạo file immutable `NNNN_description.sql` tại đây, rồi đăng ký theo thứ tự trong runner. Không sửa migration đã được áp dụng. Trước production, backup database và chạy migration trên staging.
`0029_post_inspection_assignments.sql` bổ sung inspection_type mặc định pre_rental, đổi primary key sang rental_id/vehicle_id/inspection_type. Giữ phân công trước thuê cũ, cho phép phân công sau trả riêng; backend/FE cần nâng cấp đồng bộ, runner tự phát hiện migration.
