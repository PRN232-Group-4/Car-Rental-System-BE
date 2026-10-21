# Đổi mật khẩu bắt buộc OTP — 2026-10-21

Trước đây `POST /api/account/password` chỉ kiểm tra mật khẩu cũ; OTP chỉ có ở quên mật khẩu. Nay `POST /api/account/password/otp` xác minh mật khẩu cũ và gửi mã tới email từ claim. Xác nhận đổi phải có mật khẩu cũ, OTP và mật khẩu mới 6–20 ký tự. Không còn đường Application đổi mật khẩu đăng nhập bỏ qua OTP.

Workflow riêng dùng `password-change:`, hash OTP, fingerprint hash mật khẩu hiện tại, TTL/cooldown/giới hạn sai theo RegistrationOptions. Redis compare-and-exchange chống phát mã trùng request, mất số lần sai và sử dụng mã nhiều lần. Gửi email thất bại xóa challenge vừa tạo để có thể thử lại. Sau lưu mật khẩu, thu hồi mọi phiên và xóa cookie. Hai endpoint giới hạn tổng 15 request/phút/IP/instance; rate limit phân tán vẫn theo backlog hiện có. Không migration.

HTTP errors giữ envelope 6 trường, lỗi trường trả ValidationProblemDetails; Swagger mô tả OTP bắt buộc, mã lỗi và auth. Giao diện FE dùng chung cho `/profile`, `/admin/profile`, `/staff/profile`, có gửi lại và lỗi inline.

Kiểm chứng: solution build `--no-restore -m:1 -warnaserror` đạt (0 warning/error); 82/82 test Application/HTTP/envelope/Swagger/architecture/validation/email liên quan đạt, không skip. Bao phủ OTP sai/thiếu/hết hạn/khác mục đích/khóa, password boundaries, current password, gửi lại, lỗi email, password thay ở nơi khác và concurrent single use. Thêm export Swagger tùy chọn để đồng bộ contract FE. FE: 630/630 unit test, 2 E2E Chrome desktop/mobile, build xuất tĩnh đạt. Kiểm thử dùng port/provider giả lập; chưa gửi Brevo thật hoặc chạy integration Redis thật. Docker daemon chưa chạy nên chưa kiểm tra container. Chưa deploy.
