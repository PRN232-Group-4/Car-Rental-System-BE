# Giới hạn mật khẩu 6–20 ký tự — 2026-10-07

Liên quan: [FR-001](../specs/FR-001-register-with-otp.md), [FR-003](../specs/FR-003-cookie-redis-session.md), [FR-004](../specs/FR-004-password-reset.md), [FR-007](../specs/FR-007-rbac.md), [NFR-008](../specs/NFR-008-openapi.md).

## Đã làm

- Đăng ký/đăng nhập chấp nhận password từ 6 đến 20 ký tự (bao gồm hai mốc), thay giới hạn tối đa 128.
- Dùng hằng số PasswordRules trong Application; tạo nhân viên và newPassword khi đổi/reset theo cùng giới hạn để tránh tạo credential không thể login.
- Ngoài khoảng trả 400 validation qua envelope sáu trường, thông báo `Mật khẩu phải có từ 6 đến 20 ký tự.` Không trim/cắt ngắn password; giữ hash/OTP/session hiện tại.
- Bổ sung Swagger schema filter đọc StringLength của password/newPassword ở record constructor parameter; đảm bảo minLength/maxLength thực tế là 6/20.
- Cập nhật AGENTS, specs, API reference và ví dụ nhân viên/mobile.

## Kiểm chứng

- Build solution với warnings as errors: 0 warning/error, sử dụng `--no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false`.
- **110/110 tests pass** khi lọc chính xác ApiResponseTests, UnitTests, AuthorizationTests và RefreshAuthorizationTests; runner chạy ngoài sandbox.
- Các test mới: 4 mốc 5/6/20/21 trên 5 DTO; HTTP MVC validation với DTO đăng ký/login thật; lỗi giữ envelope/boolean; Swagger minLength/maxLength.
- Lượt đầu Swagger test phát hiện giới hạn không được phát sinh từ param attributes; schema filter đã khắc phục và lượt sau toàn bộ 110 test pass.
- Không chạy lại PostgreSQL suite (lỗi xác thực local đã ghi ở change response contract); thay đổi không dùng DB/Redis/provider và không có migration.

## Giả định và giới hạn

- Đồng nhất các luồng tạo mật khẩu với giới hạn đăng nhập. `currentPassword` chỉ Required để vẫn có thể đổi từ password cũ; account cũ có password >20 cần reset để login theo rule mới.
- Các mốc kiểm tra phản ánh StringLength của .NET; không thêm yêu cầu loại ký tự/chữ hoa/chữ số ngoài yêu cầu độ dài.
- Chưa gọi deployment thật; không đổi frontend trong thay đổi BE này.
