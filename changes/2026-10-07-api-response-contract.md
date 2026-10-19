# Chuẩn hóa response JSON — 2026-10-07

Liên quan: [NFR-008](../specs/NFR-008-openapi.md), [FR-001](../specs/FR-001-register-with-otp.md), [ADR-002](../docs/decisions/ADR-002-api-response-envelope.md).

## Đã làm

- Thay response model cũ bằng `ApiResponse`: luôn có `code`, `status`, `is_success`, `data`, `message`, `error`; giữ null, boolean thật và code HTTP thực tế.
- Giữ empty success chuyển về 200 như hành vi cũ; giữ Created 201 và Accepted 202.
- Bọc lỗi controller/automatic validation qua always-run result filter, giữ ProblemDetails/validation bên trong `error`.
- Bao phủ exception, authentication/authorization, 404/405 không body bằng middleware; giữ header authentication/Allow và OAuth redirect.
- Health dùng envelope success/error; Swagger mô tả sáu trường cho success/error và mã verify registration 200.
- Cập nhật API reference, hướng dẫn mobile/rental contract, spec, progress và quyết định contract.
- Ghi contract bắt buộc vào [AGENTS.md](../AGENTS.md): sáu trường/kiểu dữ liệu, null, lỗi/auth/route, Swagger, test JSON và các ngoại lệ giao thức; sửa Definition of Done cũ để không yêu cầu ProblemDetails ở root.

## Kiểm chứng

- `dotnet build Car-Rental-System-BE.sln --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false --verbosity minimal`: pass, 0 warning/error.
- **97/97 pass** khi lọc đúng `VehicleRental.Tests.ApiResponseTests`, `UnitTests`, `AuthorizationTests`, `RefreshAuthorizationTests`.
- HTTP tests chạy Kestrel trên loopback, không cần DB/Redis/provider; kiểm tra typed/created/accepted/empty/JSON success, lỗi controller/validation và malformed JSON, 401/403/404/405/409/500/503, exception 404, chống lộ exception 500, redirect, giữ Allow, không double wrap, Swagger và trường null.
- Test runner không khởi chạy test trong sandbox; chạy ngoài sandbox thành công. Lượt lọc rộng ban đầu vô tình chọn thêm StaffAuthorizationTests: 9 test DB fail do PostgreSQL local từ chối xác thực. Đã chạy lại đúng 97 test không cần DB; chưa xác nhận lại toàn bộ DB suite.

## Giả định và giới hạn

- `code` là HTTP status dạng số; `status` là `success`/`error` theo [giả định đã ghi](../docs/open-questions.md).
- Frontend cần đọc lỗi ở `message`, `error.detail`, `error.errors` thay cho detail/errors ở root.
- Redirect/Swagger và các response giao thức không phải JSON API không đổi thành envelope.
- Không migration, không đổi nghiệp vụ rental/payment/role, chưa xác nhận deployment hoặc dịch vụ ngoài.
