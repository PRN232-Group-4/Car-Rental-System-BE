# FR-033 — Quản lý thiết bị GPS, nhận tọa độ, lịch sử vị trí và lưu giữ dữ liệu

- **Status:** Done; **Priority:** P1; **Milestone:** M2
- **Change:** [2026-10-10-gps-tracking-and-google-maps.md](../changes/2026-10-10-gps-tracking-and-google-maps.md)

## Nghiệp vụ và luồng hoạt động

### 1. Kiểm soát thiết bị gửi dữ liệu (Tracking Devices)
- Mỗi xe trong đội xe có thể gắn thiết bị GPS chuyên dụng (`tracking_devices`).
- Thiết bị được quản lý qua API: đăng ký thiết bị (`id`, `vehicle_id`, `name`), tạo API key bí mật, kích hoạt/vô hiệu hóa, hoặc gán lại xe.
- API key được hash bằng SHA-256 trước khi lưu trong database. Khóa gốc chỉ được trả về một lần duy nhất lúc đăng ký.
- Quản lý thiết bị yêu cầu quyền `Management` (Admin hoặc Manager).

### 2. API nhận tọa độ (Location Telemetry Ingestion)
- Endpoint `POST /api/tracking/ingest` nhận dữ liệu tọa độ từ thiết bị qua header `X-Device-Id` và `X-Device-Key` (hoặc body).
- Xác thực thiết bị: kiểm tra device ID tồn tại, hash khóa khớp với cơ sở dữ liệu, và trạng thái thiết bị đang active. Nếu sai trả 401 hoặc 403 theo chuẩn envelope.
- Nhận mảng tọa độ `positions` hỗ trợ offline buffering / gửi theo lô. Mỗi điểm gồm: `lat`, `lng`, `recordedAt`, `speedKmh`, `heading`, `accuracyM`.
- Điểm mới nhất sẽ cập nhật bảng `vehicle_last_positions` để tra cứu vị trí tức thời nhanh chóng.
- Dữ liệu vị trí được ghi vào bảng `vehicle_positions` phục vụ truy xuất lịch sử.
- Đồng thời cập nhật `last_ping_at` trên thiết bị.
- Kích hoạt quy trình kiểm tra sự kiện thời gian thực: vi phạm geofence, lệch lộ trình thuê (FR-035).

### 3. Lịch sử vị trí (Location History)
- Endpoint `GET /api/tracking/vehicles/{vehicleId}/history` cho phép truy vấn lịch sử vị trí của xe theo khoảng thời gian (`from`, `to`).
- Hỗ trợ phân trang chuẩn: `page` (mặc định 1), `pageSize` (mặc định 20, tối đa 100), trả envelope chứa `items`, `page`, `pageSize`, `totalCount`.
- Endpoint `GET /api/tracking/vehicles/{vehicleId}/last-position` trả về vị trí gần nhất của xe.
- Quyền truy cập: Admin, Manager, CustomerService, Maintenance, hoặc Driver được phân công xe đó.

### 4. Chính sách lưu giữ dữ liệu (Data Retention Policy)
- Bảng `vehicle_positions` sinh dữ liệu liên tục với khối lượng lớn.
- Thiết lập thời gian lưu giữ (mặc định 30 ngày, cấu hình qua `TelemetryRetention:RetentionDays`).
- Định kỳ theo ngày (hoặc cấu hình `TelemetryRetention:PurgeIntervalHours`), hosted service nền `TelemetryRetentionHostedService` tự động dọn dẹp các bản ghi vị trí cũ hơn thời điểm cắt (cutoff time) theo từng lô (`PurgeBatchSize`, mặc định 5.000 dòng/lô) để tránh lock database.
- Cung cấp API `POST /api/tracking/retention/purge` cho Admin để kích hoạt dọn dẹp thủ công khi cần.

## Acceptance criteria

- [x] Thiết bị gửi tọa độ được xác thực qua device ID và API key hash, từ chối thiết bị không hợp lệ hoặc inactive.
- [x] Ingestion API hỗ trợ nhận batch tọa độ, validate vĩ độ (-90 đến 90), kinh độ (-180 đến 180).
- [x] Cập nhật bảng vị trí gần nhất và ghi nhận lịch sử di chuyển.
- [x] API lịch sử vị trí có lọc thời gian và phân trang theo chuẩn (`page`, `pageSize`, `items`, `totalCount`).
- [x] Dọn dẹp dữ liệu cũ theo chính sách lưu giữ định kỳ qua background service và API purge thủ công.
- [x] Tất cả API tuân thủ envelope 6 trường và success message theo ngữ cảnh.
