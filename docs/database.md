# Database

API không chạy migration tự động khi khởi động. PostgreSQL phải có schema trước khi deploy; script tham khảo/khởi tạo thủ công nằm tại `docs/db/Init.sql`. Điều này tránh việc startup Railway bị dừng bởi migration checksum.

`users.email` đã unique; đăng ký transaction tạo `users`, `customers` và role `Customer`. Bổ sung index chỉ sau khi có workload thực tế và ghi lý do vào ADR/change.

## Quản lý Lịch Đặt Xe & Tính Khả Dụng (Migration 0011)

### 1. Bảng `vehicle_bookings`
Bảng đồng bộ tự động lưu trữ các khoảng thời gian xe bị chiếm chỗ bởi các lượt thuê đang hoạt động hoặc chuẩn bị nhận xe:
- `rental_id`: Khóa ngoại tham chiếu `rentals.id`, UNIQUE.
- `vehicle_id`: Khóa ngoại tham chiếu `vehicles.id`.
- `starts_at`, `ends_at`: Mốc thời gian bắt đầu và kết thúc của lượt thuê.
- `buffer_minutes`: Thời gian đệm vệ sinh, bảo trì giữa các chuyến (mặc định 60 phút).
- `period`: Cột STORED GENERATED biểu diễn khoảng thời gian mở rộng kèm đệm: `tsrange(starts_at, ends_at + make_interval(mins => buffer_minutes), '[)')`.
- `is_blocking`: Cờ xác định trạng thái có đang chặn giữ chỗ hay không (`pending`, `pre_inspection`, `renting`, `post_inspection`).
- `ex_vehicle_bookings_no_overlap`: Ràng buộc loại trừ `EXCLUDE USING gist (vehicle_id WITH =, period WITH &&) WHERE (is_blocking)` ngăn chặn triệt để đặt trùng lịch ở cấp độ cơ sở dữ liệu.

### 2. Triggers Đồng Bộ
- `trg_hour_detail_sync_booking`: Chạy sau khi INSERT/UPDATE `start_time`, `end_time` trên bảng `hour_rental_detail`.
- `trg_longterm_sync_booking`: Chạy sau khi INSERT/UPDATE `start_time`, `end_time` trên bảng `longterm_rental_contract`.
- `trg_rentals_sync_booking`: Chạy sau khi UPDATE `vehicle_id` hoặc `status` trên bảng `rentals`. Khi đổi xe giữa chừng, mốc `starts_at` của xe mới được cắt ngắn an toàn `LEAST(ends_at - interval '1 minute', GREATEST(starts_at, (now() AT TIME ZONE 'utc')))` để tránh xung đột oan với lịch quá khứ của xe mới.

### 3. View Bảo Dưỡng `v_vehicle_maintenance_blocks`
Biểu diễn khoảng thời gian xe bị chặn do bảo dưỡng:
- Các trạng thái `open`, `scheduled`, `in_progress` hoặc `completed` nhưng `is_safe = false`.
- Với trạng thái `scheduled` chỉ có ngày (GMT+7), khoảng chặn được quy đổi về UTC: `scheduled_date::timestamp - interval '7 hours'` đến hết ngày đó (+1 day).

### 4. Ràng buộc Trạng thái Xe (`ck_vehicles_status`)
Ràng buộc chặt chẽ 4 trạng thái hợp lệ của xe:
`CHECK (status IN ('available', 'renting', 'maintenance', 'inactive'))`.

## Quản lý Phân Xe, Tài Xế & Đổi Xe (Migration 0012)

### 1. Bảng `vehicle_assignments`
Bảng lưu vết toàn bộ lịch sử phân bổ xe và tài xế cho từng đơn thuê:
- `rental_id`: Khóa ngoại tham chiếu `rentals.id`.
- `vehicle_id`: Khóa ngoại tham chiếu `vehicles.id`. Bất biến nghiệp vụ: với bản ghi `assigned`, `vehicle_id` luôn trùng khớp với `rentals.vehicle_id`.
- `driver_staff_id`: Khóa ngoại tham chiếu `staffs.id` (NULL đối với trường hợp khách tự lái).
- `status`: Bắt buộc NOT NULL và tuân theo ràng buộc `ck_va_status`: `CHECK (status IN ('assigned', 'reassigned', 'completed', 'cancelled'))`.
- `reason`: Lý do phân công hoặc thay đổi (`initial`, `breakdown`, `accident`, `maintenance`, `driver_unavailable`, `driver_request`, `customer_request`, `other`).
- `note`: Ghi chú bổ sung (tối đa 500 ký tự).
- `supersedes_id`: Khóa ngoại trỏ đến bản ghi assignment trước đó khi đổi tài xế hoặc đổi xe.
- `starts_at`, `ends_at`: Khoảng thời gian phân công phục vụ.
- `ended_at`: Thời điểm thực tế kết thúc nhiệm vụ (được cập nhật khi đổi người/đổi xe sang `reassigned`).
- `buffer_minutes`: Thời gian đệm nghỉ ngơi, di chuyển giữa các ca lái xe (mặc định 60 phút).
- `period`: Cột STORED GENERATED biểu diễn khoảng thời gian mở rộng kèm đệm tài xế:
  `tsrange(starts_at, ends_at + make_interval(mins => buffer_minutes), '[)')`.

### 2. Ràng buộc Loại Trừ Lịch Tài Xế (`ex_va_driver_no_overlap`)
Sử dụng extension `btree_gist`:
`CONSTRAINT ex_va_driver_no_overlap EXCLUDE USING gist (driver_staff_id WITH =, period WITH &&) WHERE (status = 'assigned' AND driver_staff_id IS NOT NULL AND period IS NOT NULL)`.
Ràng buộc này bảo đảm ở tầng cơ sở dữ liệu rằng không có tài xế nào có thể bị phân công cho 2 đơn thuê chồng chéo thời gian (kể cả thời gian đệm).

### 3. Chỉ mục Hiệu Năng
- `ix_va_driver_status`: Hỗ trợ tra cứu nhanh lịch làm việc của tài xế theo trạng thái `assigned`.
- `ix_va_vehicle`: Hỗ trợ truy vấn lịch sử hoạt động của từng phương tiện.

