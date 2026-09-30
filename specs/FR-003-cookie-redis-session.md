# FR-003 — Đăng nhập/đăng xuất bằng cookie session Redis
- **Status:** In progress
- **Priority:** P0
- **Milestone:** M2
## Quy tắc
Cookie chỉ mang session key; ticket nằm Redis, HttpOnly/Secure/SameSite=Lax, sliding 8h và absolute expiry cần cấu hình rõ. Logout/revoke xóa key.
## Acceptance criteria
- [x] Redis `ITicketStore` được đăng ký.
- [x] Google OAuth tạo/đảm bảo customer và dùng cookie ticket Redis.
- [x] Login/logout, absolute expiry cấu hình được (mặc định 24 giờ) và revoke-all session.
- [ ] CSRF cho endpoint state-changing cookie-auth.
