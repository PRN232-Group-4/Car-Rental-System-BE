# FR-002 — Xác thực và gửi lại OTP
- **Status:** In progress
- **Priority:** P0
- **Milestone:** M2
## Quy tắc
OTP 6 số TTL 10 phút, tối đa 5 lần sai, resend cooldown 60 giây. Hết TTL/khóa trả lỗi an toàn, không lộ account.
## Acceptance criteria
- [x] TTL, cooldown và số lần sai được lưu Redis.
- [x] OTP hash và compare constant-time.
- [ ] Rate-limit phân tán email/IP.
