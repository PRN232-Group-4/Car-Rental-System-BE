# FR-003 — Đăng nhập/đăng xuất bằng access/refresh token cookie
- **Status:** In progress
- **Priority:** P0
- **Milestone:** M2

## Quy tắc

`access_token` là JWT ngắn hạn và `refresh_token` là token opaque dài hạn; cả hai ở cookie `HttpOnly`, `Secure`, `SameSite=Lax`. Redis chỉ lưu hash refresh token. Refresh token là single-use và phải rotation khi gọi refresh. Logout/revoke xóa refresh-token record; access token cùng session id bị vô hiệu ngay.

## Acceptance criteria

- [x] JWT bearer đọc `access_token` từ HttpOnly cookie và kiểm tra session id trong Redis.
- [x] Google OAuth tạo/đảm bảo customer và trả cùng cặp token cookie.
- [x] Login, refresh rotation, logout, logout-all và revoke token khi đổi/reset mật khẩu.
- [ ] CSRF cho endpoint state-changing cookie-auth.
