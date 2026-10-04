# FR-027 — Báo sự cố
- **Status:** Staff/Driver implemented; Customer reporting not started; **Priority:** P1; **Milestone:** M7

Staff báo damage riêng; Driver báo breakdown cho rental đang renting mình được phân công. Lưu reporter_staff_id, vehicle_id, rental_id, mô tả, image_urls. Manager/Admin đọc toàn bộ; nhân viên đọc report mình tạo.
Lifecycle: open → assigned (Manager giao Maintenance) → resolved. Breakdown resolved qua đổi xe/tài; damage resolve sau bảo trì hoàn tất hoặc Manager review nếu không cần bảo trì. Không tự tính phí. Xem FR-031.
