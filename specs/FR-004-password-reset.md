# FR-004 — Quên/đặt lại mật khẩu bằng OTP
- **Status:** In progress; **Priority:** P0; **Milestone:** M2
Reset qua OTP, thay hash và revoke toàn bộ session.
Mật khẩu mới khi đổi/reset dùng cùng giới hạn 6–20 ký tự như đăng ký/đăng nhập để không tạo mật khẩu không thể đăng nhập. `currentPassword` giữ kiểm tra bắt buộc và xác minh hash, không áp giới hạn mới lên mật khẩu cũ.
## API
| Method | Path | Auth | Mô tả |
|---|---|---|---|
| POST | /api/account/password-resets | No | yêu cầu OTP reset; luôn trả 202 để tránh enumeration |
| POST | /api/account/password-resets/verify | No | xác thực OTP và đặt mật khẩu mới |
## Acceptance
- [x] Email OTP dùng cùng HTML thương hiệu Thong Dong và text dự phòng với đăng ký; tiêu đề/nội dung chung cho xác thực tài khoản, không ghi sai mục đích là đăng ký.
- [x] Redis key riêng cho reset, TTL/cooldown/tối đa số lần sai.
- [x] Đổi hash và revoke toàn bộ ticket Redis khi thành công.
- [ ] Có integration test cho revoke session.
