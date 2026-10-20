# FR-027 — Báo sự cố
- **Status:** Staff/Driver implemented; Customer reporting not started; **Priority:** P1; **Milestone:** M7

Staff báo damage riêng; Driver báo breakdown cho rental đang renting mình được phân công. Lưu reporter_staff_id, vehicle_id, rental_id, mô tả, image_urls. Manager/Admin đọc toàn bộ; nhân viên đọc report mình tạo.
Lifecycle: open → assigned (Manager giao Maintenance) → resolved. Breakdown resolved qua đổi xe/tài; damage resolve sau bảo trì hoàn tất hoặc Manager review nếu không cần bảo trì. Không tự tính phí. Xem FR-031.

2026-10-20: bổ sung bước tiếp nhận `in_progress`, từ chối `rejected` có lý do và mở lại review (`rejected → in_progress`). Assigned/resolved không được bỏ qua quy trình bảo trì/recovery. GET report phân trang và trả customer/rental/maintenance, timestamps, history/notes đã lưu; xem [FR-037](FR-038-operations-api-completion.md).

2026-10-10: `POST /api/media/report` cho Admin/Manager/Staff/Maintenance/CustomerService/Driver có claim `staff_id` upload JPEG/PNG/GIF/WebP tối đa 10 MB lên Cloudinary. Gửi `secureUrl` trong `imageUrls` khi tạo report; upload không tạo report, không bỏ qua kiểm tra rental/driver ở API tạo report.
