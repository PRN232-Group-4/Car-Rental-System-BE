# FR-001 — Đăng ký tài khoản có OTP
- **Status:** In progress
- **Priority:** P0
- **Milestone:** M2
- **Actors:** Customer, System
- **DB:** users, customers; **Dependencies:** FR-002, FR-028
## Luồng chính
1. Customer gửi email, tên, mật khẩu từ 6 đến 20 ký tự (bao gồm hai mốc).
2. System chuẩn hóa email, kiểm tra chưa tồn tại, tạo pending payload Redis và gửi OTP Brevo.
3. Customer xác nhận OTP; system tạo user/customer trong transaction và xóa pending data.
## API
| Method | Path | Auth | Mô tả |
|---|---|---|---|
| POST | /api/account/registrations | No | gửi OTP |
| POST | /api/account/registrations/verify | No | xác thực và tạo account |
## Acceptance criteria
- [x] Email OTP dùng HTML responsive thương hiệu Thong Dong (nền ấm, mực slate, điểm nhấn amber), có plain text dự phòng và escape tên/mã trước khi chèn HTML. Không gửi email thật khi kiểm thử.
- [x] User chỉ được tạo sau OTP đúng; password/OTP không plain text.
- [x] Email duplicate trả 409; validation giữ ProblemDetails trong `error` của [response envelope](NFR-008-openapi.md).
- [x] Đăng ký chấp nhận mật khẩu 6/20 ký tự; từ chối 5/21 ký tự bằng 400 có envelope validation. Xem [change](../changes/2026-10-07-password-length.md).
- [x] Gửi, gửi lại và xác nhận đăng ký giới hạn riêng từng endpoint tối đa 5 request/60 giây/IP/instance; request thứ 6 trả 429 có envelope và Retry-After. Cấu hình `Registration:MaxRequests` (1–5), `RequestWindowSeconds` (1–3600).
- [ ] Có integration test Redis/PostgreSQL/Brevo fake.
- [ ] Rate-limit distributed theo email/IP.
