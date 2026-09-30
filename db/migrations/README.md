# SQL migrations

`0001_initial_schema` là `docs/db/Init.sql`, được migration runner áp dụng và ghi checksum vào `schema_migrations`.

Mỗi thay đổi schema tiếp theo tạo file immutable `NNNN_description.sql` tại đây, rồi đăng ký theo thứ tự trong runner. Không sửa migration đã được áp dụng. Trước production, backup database và chạy migration trên staging.
