# 2026-10-14 — Khôi phục mặc định kết nối khi chạy local
- **FR/NFR:** NFR-005
- **Loại:** fix, docs

## Đã làm
- Development tự dùng PostgreSQL và Redis tại `localhost` theo cấu hình Docker Compose khi không có connection string tường minh.
- Production vẫn yêu cầu cấu hình connection string tường minh.
- Bổ sung hướng dẫn cấu hình JWT/Brevo và địa chỉ Swagger local.

## Chưa làm
- Chưa khởi động hoặc kiểm chứng database/Redis thật trong môi trường này vì Docker CLI không có sẵn.

## Test
- `dotnet build src\VehicleRental.WebApi\VehicleRental.WebApi.csproj --no-restore`: pass, 0 warning/error.
- Chạy WebApi với cấu hình Development và giá trị local không bí mật: khởi tạo options thành công, tới bước migration PostgreSQL rồi dừng do PostgreSQL local không chạy. Kết nối database/Redis thực tế chưa kiểm chứng.

## Quyết định/giả định
- Giữ nguyên fail-fast khi database hoặc Redis không khả dụng; chỉ mặc định endpoint local cho Development, không giả lập các dependency này.
- JWT signing key và Brevo API key vẫn phải được cấu hình qua environment variables hoặc user-secrets; không thêm secret vào Git.
