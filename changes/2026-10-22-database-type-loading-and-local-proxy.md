# 2026-10-22 — Workaround PostgreSQL metadata và proxy local

- **FR/NFR:** NFR-005.
- **Loại:** fix, config, docs.

## Thay đổi

- Thêm `Database:SkipTypeLoading` mặc định false; khi bật, datasource dùng `Server Compatibility Mode=NoTypeLoading` của Npgsql 8. Chỉ dùng cho truy vấn kiểu tích hợp; cần tắt nếu dùng enum/domain/composite tùy chỉnh.
- Bật cờ trong appsettings.Development.json local đã được Git ignore. Cờ không chứa secret; không sao chép connection string.
- Giữ nguyên dependency 8.0.5 và hành vi migration. Cập nhật spec, hướng dẫn triển khai và biến mẫu.
- Frontend đang chạy có proxy manifest cũ trỏ Railway dù `.env` trỏ localhost:6789. Cập nhật timestamp next.config.ts để Next dev nạp lại cấu hình; manifest đã trỏ localhost:6789, không thay mã frontend.

## Kiểm chứng

- `dotnet build Car-Rental-System-BE.sln --no-restore -warnaserror`: đạt, 0 warning/error.
- Truy vấn chỉ đọc dùng cấu hình Development và Npgsql 8.0.5: mở Neon trong 0,53 giây; đọc/deserialize được text[], varchar[], integer[], timestamptz, timestamp, jsonb, numeric, boolean, tsrange.
- Lịch sử database hiện tại có `0027_role_catalogue`; không chạy migration trong các phép thử này.
- Backend `/health` ở localhost:6789 trả 200. POST đăng nhập rỗng trực tiếp trả 400 trong 0,17 giây; qua Node trực tiếp khoảng 34 ms.
- Trước khi reload, cùng POST qua frontend localhost:3000 timeout quá 10 giây. Sau reload, trả validation 400 trong 0,13 giây; manifest `/api/*` trỏ localhost:6789.

## Giới hạn

- Chưa xác định nguyên nhân gốc khiến nạp metadata thất bại. Tăng timeout 60 giây và thử Npgsql 9.0.2 bằng công cụ tạm vẫn gặp lỗi; không nâng phiên bản dự án.
- Không thử tài khoản thật, không gửi email, không chạy full suite PostgreSQL/Redis hoặc deploy.
