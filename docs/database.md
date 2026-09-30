# Database

API không chạy migration tự động khi khởi động. PostgreSQL phải có schema trước khi deploy; script tham khảo/khởi tạo thủ công nằm tại `docs/db/Init.sql`. Điều này tránh việc startup Railway bị dừng bởi migration checksum.

`users.email` đã unique; đăng ký transaction tạo `users`, `customers` và role `Customer`. Bổ sung index chỉ sau khi có workload thực tế và ghi lý do vào ADR/change.
