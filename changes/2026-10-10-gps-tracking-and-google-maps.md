# 2026-10-10 — Định vị GPS, Tích hợp Google Maps, Lộ trình, Geofencing và Cảnh báo

- **FR/NFR:** FR-009, FR-010, FR-018, FR-031, FR-033, FR-034, FR-035, NFR-001, NFR-004, NFR-008
- **Loại:** feat, migration, tests, docs

## Đã làm

- **Nhận tọa độ & kiểm soát thiết bị**:
  - Migration `0015_vehicle_tracking_and_geofencing.sql` tạo các bảng `tracking_devices`, `vehicle_positions`, `vehicle_last_positions`, `geofences`, `geofence_events`, `tracking_alerts`.
  - Quản lý thiết bị (`DevicesController`): đăng ký thiết bị (`POST /api/devices`), cập nhật (`PUT /api/devices/{id}`), chi tiết và danh sách phân trang. Khóa API được hash SHA-256 trong cơ sở dữ liệu.
  - Ingestion endpoint (`POST /api/tracking/ingest`): xác thực thiết bị qua headers `X-Device-Id` và `X-Device-Key`, nhận lô tọa độ (offline buffering), cập nhật vị trí tức thời và ghi nhận lịch sử.
  - Lịch sử vị trí (`GET /api/tracking/vehicles/{id}/history`): truy vấn theo dải thời gian với phân trang chuẩn (`page`, `pageSize`, `items`, `totalCount`).
  - Vị trí gần nhất (`GET /api/tracking/vehicles/{id}/last-position`).
- **Chính sách lưu giữ dữ liệu**:
  - `TelemetryRetentionHostedService` định kỳ dọn dẹp các bản ghi vị trí cũ hơn số ngày cấu hình (`TelemetryRetention:RetentionDays`) theo từng lô (`PurgeBatchSize`).
  - Endpoint dọn dẹp thủ công cho Admin (`POST /api/tracking/retention/purge`).
- **Tích hợp Google Maps API**:
  - Infrastructure adapter `GoogleMapsClient` hỗ trợ Geocoding (`/api/map/geocode`), Reverse Geocoding (`/api/map/reverse-geocode`), Places Search (`/api/map/places`), Distance Matrix (`/api/map/distance-matrix`), và Directions (`/api/map/directions`).
  - Tự động fallback tính toán khoảng cách/vận tốc an toàn khi offline hoặc chưa cấu hình API key.
- **Truy vấn xe theo bán kính, khoảng cách và ETA**:
  - `GET /api/tracking/vehicles/nearby`: lọc xe theo bán kính bằng công thức Haversine trên SQL; khi bật `includeEta=true` tích hợp tính khoảng cách lái xe và ETA qua Google Maps.
- **Lộ trình thuê (Rental Route)**:
  - `GET /api/tracking/rentals/{rentalId}/route`: tổng hợp hành trình thực tế trong thời gian thuê, tính tổng quãng đường (mét), thời lượng (giây), vận tốc trung bình/tối đa và danh sách tọa độ breadcrumbs.
- **Geofencing & Cảnh báo**:
  - Quản lý vùng địa lý (`/api/geofences`) và phát hiện chuyển đổi trạng thái `ENTER`/`EXIT` (`/api/geofences/events`).
  - Cảnh báo vi phạm vùng cấm (`geofence_violation`) khi xe đi vào vùng prohibited hoặc rời vùng allowed.
  - Cảnh báo xe đi lệch lộ trình (`route_deviation`) qua thuật toán đo khoảng cách vuông góc tới polyline chỉ đường (`RouteGeometryService`).
  - Kiểm tra điểm trả xe (`POST /api/tracking/rentals/{rentalId}/verify-dropoff`): đối chiếu vị trí thực tế của xe với tọa độ bãi xe/trạm quy định; nếu vượt ngưỡng dung sai sẽ kích hoạt cảnh báo `wrong_return_point`.
  - Quản lý và đóng cảnh báo: `GET /api/tracking/alerts` và `POST /api/tracking/alerts/{id}/resolve`.

## Validation

- Bộ test `TrackingTests.cs`:
  - Kiểm thử `GeoPoint` (tính khoảng cách Haversine, kiểm tra bán kính, giới hạn tọa độ).
  - Kiểm thử `RouteGeometryService` (giải mã Google polyline, khoảng cách tới đoạn thẳng, phát hiện xe đi lệch).
  - Kiểm thử `GeofenceZone` (điểm trong/ngoài vùng bán kính, cờ kích hoạt).
  - Kiểm thử `TrackingValidators` (phân trang, tọa độ, bán kính, thông tin thiết bị).
  - Kiểm thử use case `TrackingService` (xác thực thiết bị, nhận batch tọa độ, kích hoạt cảnh báo geofence vi phạm vùng cấm, phát hiện trả sai điểm, tổng hợp lộ trình chuyến thuê, dọn dẹp dữ liệu lưu giữ).
- Architecture test suite: 100% pass, tuân thủ Clean Architecture dependency direction.
- Response contract: đủ 6 trường JSON bắt buộc (`code`, `status`, `is_success`, `data`, `message`, `error`), success message theo ngữ cảnh từng endpoint trong `ApiSuccessResponseFilter`.

## Giới hạn

- Google Maps API key được cấu hình qua options; môi trường test/dev ngoại tuyến sử dụng cơ chế fallback mô phỏng.
- Bản đồ polygon GeoJSON lưu dạng text; spatial query bán kính tròn dùng công thức Haversine trên PostgreSQL.
