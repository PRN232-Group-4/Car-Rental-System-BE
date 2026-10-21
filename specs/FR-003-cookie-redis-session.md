# FR-003 — Đăng nhập/đăng xuất bằng access/refresh token cookie
- **Status:** In progress
- **Priority:** P0
- **Milestone:** M2

## Quy tắc

Mật khẩu đăng nhập phải từ 6 đến 20 ký tự, bao gồm hai mốc; ngoài khoảng trả 400 validation trước khi truy xuất account/Redis. Tài khoản cũ có mật khẩu dài hơn 20 ký tự cần reset về giới hạn mới; không cắt ngắn mật khẩu để xác thực.

`access_token` là JWT ngắn hạn và `refresh_token` là token opaque dài hạn; cả hai ở cookie `HttpOnly`, `Secure`, `SameSite=Lax`. Redis chỉ lưu hash refresh token. Refresh token là single-use và phải rotation khi gọi refresh. Logout/revoke xóa refresh-token record; access token cùng session id bị vô hiệu ngay.

## Acceptance criteria

- [x] JWT bearer đọc `access_token` từ HttpOnly cookie và kiểm tra session id trong Redis.
- [x] Google OAuth tạo/đảm bảo customer và trả cùng cặp token cookie.
- [x] Login, refresh rotation, logout, logout-all và revoke token khi đổi/reset mật khẩu.
- [x] Login chấp nhận độ dài 6/20 để tiếp tục xác thực; từ chối 5/21 ký tự bằng 400 có envelope validation. Xem [change](../changes/2026-10-07-password-length.md).
- [x] Login tối đa 5 request/60 giây/IP/instance; request thứ 6 trả 429. Sai mật khẩu hoặc email không tồn tại có thêm counter Redis riêng theo email chuẩn hóa: tối đa 5 lần trong 10 phút tính từ lần thử đầu; lần sai thứ 5 và các lần tiếp theo trả 429, kể cả mật khẩu đúng khi đang khóa. Xác thực thành công trước ngưỡng xóa counter; hết cửa sổ tự thử lại được. Đặt chỗ bằng CAS trước đọc account/hash để request đồng thời không vượt 5 lần kiểm tra. Cấu hình `Registration:MaxPasswordAttempts` (1–5), `PasswordAttemptWindowMinutes` (1–60).
- [ ] CSRF cho endpoint state-changing cookie-auth.

## Phân quyền cập nhật 2026-10-03

JWT có staff_id, department_id, department_name, facility_id (nếu có), authorization_version và roles. GET /api/account/profile trả thông tin tương ứng cho frontend. Access/refresh token gắn phiên bản quyền; thay role, thông tin staff, mật khẩu hoặc active flag làm token cũ mất hiệu lực. Migration 0008 bắt buộc trước khi chạy phiên bản này.
