# FR-015 — Pre-rental inspection
- **Status:** Implemented; **Priority:** P0; **Milestone:** M4

Một pre-inspection/rental/vehicle, evidence bất biến; is_ok=true của xe hiện tại mới được renting. Fail giữ pending, khóa xe để Manager thay xe cùng số chỗ; không tự hủy rental, tạo bảo trì hoặc tính phí.
Admin/Maintenance có staff profile thực hiện. staffId trong body phải trùng claim; luôn lưu người thực hiện từ principal. Xe thay thế có endpoint inspection riêng. Xem FR-031.

2026-10-10: `POST /api/media/inspection` cho cùng quyền Inspection upload JPEG/PNG/GIF/WebP tối đa 10 MB lên Cloudinary; dùng `secureUrl` trong `imageUrls` của API inspection hiện có. Upload không tự tạo hay sửa evidence.
