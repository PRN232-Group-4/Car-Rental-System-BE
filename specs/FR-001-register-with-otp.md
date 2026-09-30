# FR-001 — Đăng ký tài khoản có OTP
- **Status:** In progress
- **Priority:** P0
- **Milestone:** M2
- **Actors:** Customer, System
- **DB:** users, customers; **Dependencies:** FR-002, FR-028
## Luồng chính
1. Customer gửi email, tên, mật khẩu hợp lệ.
2. System chuẩn hóa email, kiểm tra chưa tồn tại, tạo pending payload Redis và gửi OTP Brevo.
3. Customer xác nhận OTP; system tạo user/customer trong transaction và xóa pending data.
## API
| Method | Path | Auth | Mô tả |
|---|---|---|---|
| POST | /api/account/registrations | No | gửi OTP |
| POST | /api/account/registrations/verify | No | xác thực và tạo account |
## Acceptance criteria
- [x] User chỉ được tạo sau OTP đúng; password/OTP không plain text.
- [x] Email duplicate trả 409; validation trả ProblemDetails.
- [ ] Có integration test Redis/PostgreSQL/Brevo fake.
- [ ] Rate-limit distributed theo email/IP.
