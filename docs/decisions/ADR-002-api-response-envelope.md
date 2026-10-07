# ADR-002 — API response envelope

- Status: Accepted
- Ngày: 2026-10-07

## Bối cảnh

Người dùng yêu cầu mọi response JSON có `code`, `is_success`, `data`, `message`, `error`, sau đó bổ sung `status`. Contract cũ chỉ bọc success, còn lỗi trả ProblemDetails trực tiếp hoặc body rỗng.

## Quyết định

Thống nhất sáu trường bắt buộc: `code` là số HTTP status thực tế, `status` là `success`/`error`, `is_success` là boolean, `data` là dữ liệu thành công hoặc null, `message` là thông báo, `error` là ProblemDetails/chi tiết lỗi hoặc null. Trường null luôn được serialize. Giữ mã HTTP và hành vi empty success chuyển về 200 hiện có. ProblemDetails vẫn được giữ bên trong `error` để bảo toàn validation details.

MVC always-run result filter bao phủ kết quả controller và validation. Exception handler và status-code pages bao phủ lỗi bên ngoài filter, gồm authentication/authorization và route không tồn tại. Minimal health endpoint trả envelope trực tiếp. Không bọc redirect, HTML, Swagger JSON hoặc các response giao thức không phải JSON API.

## Phương án đã cân nhắc

Chỉ thêm trường vào success sẽ để lỗi sai contract. Buffer và viết lại mọi response có thể ảnh hưởng stream, redirect và Swagger; dùng các điểm xử lý kết quả/lỗi của framework thay thế.

## Hệ quả

Client xử lý lỗi cần đọc `message` hoặc `error.detail`/`error.errors` thay cho `detail`/`errors` ở root. Redirect OAuth giữ nguyên để đăng nhập hoạt động. NFR-008 vẫn cần các phần versioning/auth schema chưa hoàn thành.
