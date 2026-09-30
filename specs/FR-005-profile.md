# FR-005 — Hồ sơ cá nhân
- **Status:** In progress; **Priority:** P1; **Milestone:** M2
Customer sửa thông tin/avatar/mật khẩu của chính mình. Cloudinary avatar upload endpoint đã có.
## API
| Method | Path | Auth | Mô tả |
|---|---|---|---|
| GET | /api/account/profile | Cookie | đọc hồ sơ hiện tại |
| PUT | /api/account/profile | Cookie | cập nhật tên, giới tính, điện thoại của chính mình |
| POST | /api/account/password | Cookie | đổi mật khẩu và revoke toàn bộ session |
## Acceptance
- [x] Endpoint chỉ dùng email từ claim cookie, không nhận định danh account từ client.
- [x] Avatar giới hạn ảnh 5 MB tại endpoint `/api/media/avatar`.
- [ ] Audit thay đổi mật khẩu.
