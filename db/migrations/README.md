# SQL migrations

`0019_rental_policy_usage.sql` bổ sung index `rentals(policy_id)` cho số lần thuê theo policy. Runner hiện tự tìm các file `*.sql` theo tên; không cần đăng ký thêm trong mã. Migration chỉ thêm index, không chỉnh dữ liệu hay tự tắt policy active trùng có sẵn.

`0001_initial_schema` là `docs/db/Init.sql`, được migration runner áp dụng và ghi checksum vào `schema_migrations`. Một số database legacy đã áp dụng baseline trước khi `Init.sql` bị cập nhật; runner sẽ giữ checksum cũ của riêng `0001` và tiếp tục áp dụng migration `0002+`. Checksum được chuẩn hóa line ending để cùng SQL chạy trên Windows/Linux không bị khác hash; checksum nội dung migration vẫn được kiểm tra nghiêm ngặt.

Mỗi thay đổi schema tiếp theo tạo file immutable `NNNN_description.sql` tại đây, rồi đăng ký theo thứ tự trong runner. Không sửa migration đã được áp dụng. Trước production, backup database và chạy migration trên staging.
