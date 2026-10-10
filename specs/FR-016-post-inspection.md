# FR-016 — Post-rental inspection
- **Status:** Implemented; **Priority:** P0; **Milestone:** M4

Post-inspection xe hiện tại bắt buộc trước completed. Ghi nhận an toàn, km, xăng/pin, hình, ghi chú, staff_id từ principal. Admin/Maintenance có staff profile thực hiện, evidence bất biến.
Fail khóa xe. Staff tạo damage report riêng; Manager giao bảo trì. Inspection không tự tạo maintenance, phí hư hại hoặc hoàn cọc. Refund qua API payment riêng. Xem FR-031.

2026-10-10: dùng `POST /api/media/inspection` để upload ảnh Cloudinary trước khi lưu `imageUrls`, cùng quyền Inspection, JPEG/PNG/GIF/WebP tối đa 10 MB. Không thay đổi evidence đã lưu.
