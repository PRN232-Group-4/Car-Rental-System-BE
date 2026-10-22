# FR-016 — Post-rental inspection
- **Status:** Implemented; **Priority:** P0; **Milestone:** M4

Post-inspection xe hiện tại bắt buộc trước completed. Ghi nhận an toàn, km, xăng/pin, hình, ghi chú, staff_id từ principal. Admin/Maintenance có staff profile thực hiện, evidence bất biến.
Fail khóa xe. Staff tạo damage report riêng; Manager giao bảo trì. Inspection không tự tạo maintenance, phí hư hại hoặc hoàn cọc. Refund qua API payment riêng. Xem FR-031.

2026-10-10: dùng `POST /api/media/inspection` để upload ảnh Cloudinary trước khi lưu `imageUrls`, cùng quyền Inspection, JPEG/PNG/GIF/WebP tối đa 10 MB. Không thay đổi evidence đã lưu.
# Phân công và xác nhận sau trả xe (2026-10-22)

Ở post_inspection, Admin/Manager phân công kỹ thuật viên Maintenance đang hoạt động hoặc Admin xác nhận Đã inspect khi xe thực tế đạt. Dùng cùng API inspection-assignment với query inspectionType=post_rental; mặc định pre_rental giữ tương thích. Phân công/kết quả hai giai đoạn độc lập theo rental/vehicle/type. Sau trả không tự đạt cho đơn có tài xế, không tự hoàn tất hoặc thanh toán. Không ghi đè bất kỳ biên bản sau thuê đã có (kể cả không đạt), không phân công ở sai trạng thái. Lưu người xác nhận và nguồn admin_confirmation; dữ liệu đo/ảnh không được giả lập. Biên bản kỹ thuật đầy đủ hiện hữu vẫn dùng để ghi nhận hư hỏng/không đạt.
