# FR-002 — Xác thực và gửi lại OTP
- **Status:** In progress
- **Priority:** P0
- **Milestone:** M2
## Quy tắc
OTP 6 số TTL 10 phút, tối đa 5 lần sai, resend cooldown 60 giây. Hết TTL/khóa trả lỗi an toàn, không lộ account.
## API
| Method | Path | Auth | Mô tả |
|---|---|---|---|
| POST | /api/account/registrations/resend | No | gửi lại OTP của đăng ký đang chờ |
## Acceptance criteria
- [x] TTL, cooldown và số lần sai được lưu Redis.
- [x] OTP hash và compare constant-time.
- [x] Resend thay OTP cũ, reset số lần thử và giữ TTL còn lại.
- [x] Lần OTP sai thứ 5 trả 429 và giữ mã bị khóa đến hết TTL hoặc gửi lại sau cooldown; số lần sai/tiêu thụ mã dùng Redis compare-and-exchange nên request đồng thời không mất counter hoặc sử dụng mã nhiều lần. `MaxVerificationAttempts` chỉ cho phép 1–5.
- [ ] Rate-limit phân tán email/IP.
