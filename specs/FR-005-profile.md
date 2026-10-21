# FR-005 — Hồ sơ cá nhân
- **Status:** In progress; **Priority:** P1; **Milestone:** M2
Customer sửa thông tin/avatar/mật khẩu của chính mình. Cloudinary avatar upload endpoint đã có.
## API
| Method | Path | Auth | Mô tả |
|---|---|---|---|
| GET | /api/account/profile | Cookie | đọc hồ sơ hiện tại |
| PUT | /api/account/profile | Cookie | cập nhật tên, giới tính, điện thoại của chính mình |
| POST | /api/account/password/otp | Cookie | kiểm tra currentPassword, gửi OTP về email từ claim |
| POST | /api/account/password | Cookie | kiểm tra currentPassword + otp + newPassword, đổi mật khẩu và revoke toàn bộ session |
## Acceptance
- [x] Endpoint chỉ dùng email từ claim cookie, không nhận định danh account từ client.
- [x] Avatar giới hạn ảnh 5 MB tại endpoint `/api/media/avatar`.
- [x] Đổi mật khẩu bắt buộc OTP 6 số riêng (`password-change:`), TTL 10 phút, cooldown 60 giây, tối đa 5 lần sai (theo RegistrationOptions); không dùng OTP đăng ký/reset.
- [x] OTP chỉ dùng một lần bằng thao tác Redis nguyên tử; mã gắn với hash mật khẩu hiện tại, không đổi hash/thu hồi phiên khi mã sai/hết hạn/thiếu. Gửi lại thay mã cũ; email lấy từ claim, rate limit theo IP.
- [x] Giao diện hồ sơ customer/admin/staff có gửi/gửi lại OTP, lỗi inline, mật khẩu mới 6–20 ký tự; đổi thành công yêu cầu đăng nhập lại.
- [ ] Audit thay đổi mật khẩu.
