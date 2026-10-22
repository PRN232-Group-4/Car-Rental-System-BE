# NFR-005 — Logging/health
- **Status:** In progress
Database health endpoint exists. Remaining: structured logging, correlation ID, Redis readiness.

## Khởi tạo kết nối PostgreSQL — 2026-10-22

- Mặc định Npgsql tiếp tục nạp metadata PostgreSQL đầy đủ.
- Khi timeout tại `LoadBackendTypes` nhưng kết nối với các kiểu tích hợp vẫn hoạt động, có thể bật `Database:SkipTypeLoading=true` để dùng chế độ `NoTypeLoading` của Npgsql 8.
- Đây là workaround cấu hình theo môi trường; chỉ dùng khi ứng dụng không cần kiểu enum/domain/composite tự định nghĩa. Không ghi thêm connection string hoặc secret để bật workaround.
- Acceptance: bật cờ phải mở được kết nối và đọc các kiểu tích hợp đang dùng; không bật cờ giữ cấu hình nạp metadata mặc định. Migration vẫn chạy theo `Database:ApplyMigrations`.
