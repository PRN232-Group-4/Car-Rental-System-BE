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
