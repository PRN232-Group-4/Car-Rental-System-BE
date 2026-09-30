# Database

Nguồn migration đầu tiên: `docs/db/Init.sql`; PostgreSQL 16. Khi API khởi động, `DatabaseMigrationHostedService` tạo `schema_migrations`, kiểm tra SHA-256 và chạy `0001_initial_schema` trong transaction nếu database trống. Docker Compose không còn inject SQL vào container PostgreSQL để tránh race/duplicate với runner.

Database có schema cũ nhưng chưa có history sẽ **dừng an toàn**. Chỉ sau khi xác nhận schema khớp, đặt tạm `Database__BaselineExistingSchema=true` cho một lần khởi động để ghi baseline; sau đó trả về `false`. Migration mới đặt tại `db/migrations/NNNN_description.sql`; không sửa migration đã applied.

`users.email` đã unique; đăng ký transaction tạo `users` rồi `customers`. Bổ sung index chỉ sau khi có workload thực tế và ghi lý do vào ADR/change.
