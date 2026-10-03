# FR-009 — Loại xe và xe
- **Status:** In progress; **Priority:** P1; **Milestone:** M3
CRUD vehicle type, vehicle details/images. Cloudinary storage service đã sẵn sàng; vehicle upload endpoint còn pending. Acceptance: subtype đúng vehicle type, status transition hợp lệ, pagination.

## Phân quyền cập nhật 2026-10-03

Phân quyền: đọc catalog public; tạo/sửa loại xe, xe và upload ảnh xe cho Admin/Manager; xóa loại xe chỉ Admin. Staff/Driver không tự có quyền quản lý catalog.
