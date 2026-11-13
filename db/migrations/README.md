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

`0030_customer_licenses_and_vehicle_classes.sql` thêm allowed_license_types, bảng customer_licenses và view tương thích; backfill bằng đã có, giữ nguyên duyệt. Migration runner tự phát hiện; triển khai BE và FE đồng bộ.

`0032_rental_expiry.sql` thêm `rentals.expire_at`/`expired_at`, mở rộng CHECK trạng thái với `expired`, index theo `expire_at` và backfill cho đơn đang mở; `fn_rental_is_blocking` không đổi tập trạng thái đang giữ lịch nên đơn hết hạn tự giải phóng `vehicle_bookings`. Runner tự phát hiện file mới.

`0034_foreign_keys.sql` thêm FK cho mọi quan hệ logic (ADR-014): tạo `NOT VALID` rồi thử `VALIDATE`; dữ liệu cũ mồ côi chỉ sinh WARNING, FK vẫn ràng buộc dữ liệu ghi mới. Dọn dữ liệu rồi chạy `ALTER TABLE <bảng> VALIDATE CONSTRAINT fk_<bảng>_<cột>`. CASCADE chỉ cho bảng con thuần (user_roles, ảnh/chi tiết xe, telemetry); runner tự phát hiện.

Kiểm chứng ngày 2026-11-13: sau toàn bộ migration, 46 bảng nghiệp vụ có tổng 88 FK (82 từ 0034, 6 từ 0013/0017); không thiếu FK so với EF model và các cột tham chiếu. `ForeignKeySchemaTests` kiểm tra catalog, validation, idempotency và nâng cấp dữ liệu legacy mồ côi. Các bảng gốc/danh mục độc lập và lịch sử migration không cần FK đi ra; `customer_loyalty_transactions.ref_id` là tham chiếu đa hình.
