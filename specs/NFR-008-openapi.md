# NFR-008 — OpenAPI
- **Status:** In progress
Swagger non-production exists; log startup in ra URL Swagger; AGENT bắt buộc API mới có metadata OpenAPI. Remaining: versioning, API examples and auth schema.

## Tài liệu Markdown — 2026-10-07

- [x] Có [API reference tiếng Việt](../docs/API.md) đối chiếu controller/DTO/policy/response filter/repository.
- [x] Liệt kê đủ 90 controller endpoints; bổ sung root, health và callback Google middleware.
- [x] Mô tả cookie authentication, quyền và scope dữ liệu, request/response, mã HTTP thực tế và ví dụ frontend.
- [ ] Xác nhận tài liệu bằng HTTP smoke test trên hệ thống đang chạy.

Đây là bổ sung tài liệu, không đổi API hoặc đánh dấu hoàn thành versioning/auth schema/examples trong Swagger. Xem [change](../changes/2026-10-07-api-documentation.md).

## Response contract — 2026-10-07

Theo yêu cầu người dùng, response JSON có đủ sáu trường `code`, `status`, `is_success`, `data`, `message`, `error`. `code` là mã HTTP dạng số; `status` là `success`/`error`; `is_success` luôn là JSON boolean. Thành công có `error: null`; lỗi có `data: null` và giữ ProblemDetails/validation details trong `error`. Redirect OAuth/HTTPS và nội dung Swagger vẫn giữ giao thức riêng.

- [x] Success/empty success có đủ sáu trường, code khớp HTTP thực tế.
- [x] Lỗi controller, validation tự động, exception, 401/403 và 404/405 có cùng envelope.
- [x] Swagger mô tả đủ sáu trường cho success/error và kiểu boolean.
- [x] Test HTTP/serialization xác nhận contract, không bỏ trường null.

Quyết định: [ADR-002](../docs/decisions/ADR-002-api-response-envelope.md). Thay đổi: [response contract](../changes/2026-10-07-api-response-contract.md).

## Password validation schema — 2026-10-07

Swagger đọc StringLength của Password/NewPassword ở constructor parameter của record qua schema filter: minLength=6, maxLength=20. HTTP/Swagger tests xác nhận DTO đăng ký/đăng nhập và unit tests xác nhận giới hạn đồng nhất khi tạo nhân viên/đổi/reset mật khẩu. Xem [change](../changes/2026-10-07-password-length.md).
