# Database

API không chạy migration tự động khi khởi động. PostgreSQL phải có schema trước khi deploy; script tham khảo/khởi tạo thủ công nằm tại `docs/db/Init.sql`. Điều này tránh việc startup Railway bị dừng bởi migration checksum.

`users.email` đã unique; đăng ký transaction tạo `users`, `customers` và role `Customer`. Bổ sung index chỉ sau khi có workload thực tế và ghi lý do vào ADR/change.

## Migration 0011
Thêm vehicles.license_plate (IF NOT EXISTS), driver_shifts, vehicle_replacements; reports thêm tác giả staff/xe/evidence/audit. Unique inspection thành rental + vehicle + type. Không sửa migration cũ; áp dụng 0011 qua startup migration.
