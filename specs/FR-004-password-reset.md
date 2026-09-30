# FR-004 — Quên/đặt lại mật khẩu bằng OTP
- **Status:** In progress; **Priority:** P0; **Milestone:** M2
Reset qua OTP, thay hash và revoke toàn bộ session.
## API
| Method | Path | Auth | Mô tả |
|---|---|---|---|
| POST | /api/account/password-resets | No | yêu cầu OTP reset; luôn trả 202 để tránh enumeration |
| POST | /api/account/password-resets/verify | No | xác thực OTP và đặt mật khẩu mới |
## Acceptance
- [x] Redis key riêng cho reset, TTL/cooldown/tối đa số lần sai.
- [x] Đổi hash và revoke toàn bộ ticket Redis khi thành công.
- [ ] Có integration test cho revoke session.
