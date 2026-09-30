# 2026-09-30 — Google OAuth và Cloudinary media storage
- **FR/NFR:** FR-003, FR-005, FR-009, NFR-004, NFR-008
- **Loại:** feat, docs
## Đã làm
- Google OAuth `/api/auth/google` → `/signin-google` → Redis cookie; Google account được tạo/đảm bảo customer local.
- Cloudinary signed server-side image storage; authenticated `POST /api/media/avatar` upload ảnh tối đa 5 MB và cập nhật `users.avatar`.
- Swagger metadata, Railway/local configuration guide và `.env.example`.
## Chưa làm
- Local email/password login/logout, RBAC role mapping from Google, vehicle image CRUD endpoint, Cloudinary asset cleanup khi thay avatar.
## Test
- Cần restore/build sau khi tải NuGet Google và Cloudinary; không thể integration test OAuth/Cloudinary khi không có secrets callback thực.
