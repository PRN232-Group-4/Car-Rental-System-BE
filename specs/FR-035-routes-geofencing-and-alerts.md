# FR-035 — Lộ trình thuê, sự kiện vào/rời khu vực (Geofencing) và cảnh báo xe đi lệch hoặc trả sai điểm

- **Status:** Done; **Priority:** P1; **Milestone:** M2
- **Change:** [2026-10-10-gps-tracking-and-google-maps.md](../changes/2026-10-10-gps-tracking-and-google-maps.md)

## Nghiệp vụ và luồng hoạt động

### 1. Lộ trình thuê (Rental Route Tracking)
- Cho một lượt thuê (`rentalId`):
  - Tra cứu thời gian bắt đầu (`start_time`), thời gian kết thúc hoặc thời điểm hiện tại nếu đang thuê (`end_time`).
  - Lấy toàn bộ tọa độ telemetry của xe thuộc khoảng thời gian thuê đó.
  - Tổng hợp các chỉ số hành trình:
    - Tổng số điểm GPS ghi nhận (`totalPoints`).
    - Tổng quãng đường di chuyển thực tế (`totalDistanceMeters`) tính bằng tổng khoảng cách giữa các điểm kế tiếp.
    - Thời lượng di chuyển (`durationSeconds`).
    - Tốc độ trung bình (`averageSpeedKmh`) và tốc độ tối đa (`maxSpeedKmh`).
    - Điểm xuất phát (`startPosition`) và điểm kết thúc/hiện tại (`endPosition`).
    - Danh sách các điểm mốc tọa độ phục vụ vẽ polyline lộ trình trên bản đồ.
- Endpoint `GET /api/tracking/rentals/{rentalId}/route`. Quyền truy cập: Admin, Manager, CustomerService, hoặc chính Customer thuê chuyến đó.

### 2. Sự kiện vào/rời khu vực (Geofencing Enter/Exit Events)
- Cho phép định nghĩa các vùng địa lý (`geofences`):
  - Loại vùng (`zoneType`): `allowed` (vùng hoạt động được phép), `prohibited` (vùng cấm di chuyển vào), `facility` (trạm/bãi xe).
  - Tọa độ tâm (`centerLat`, `centerLng`) và bán kính (`radiusMeters`), hoặc GeoJSON polygon.
  - Quản lý qua API: `GET /api/geofences`, `POST /api/geofences`.
- Khi thiết bị gửi tọa độ mới:
  - So sánh vị trí mới với vị trí trước đó của xe đối với các vùng địa lý đang kích hoạt.
  - Phát hiện chuyển trạng thái:
    - Từ ngoài vào trong: Ghi nhận sự kiện `ENTER`.
    - Từ trong ra ngoài: Ghi nhận sự kiện `EXIT`.
  - Lưu vào bảng `geofence_events`.
  - Nếu xe đi vào vùng `prohibited` hoặc rời khỏi vùng `allowed`, tự động sinh cảnh báo vi phạm `geofence_violation`.
- API tra cứu sự kiện geofence: `GET /api/geofences/events` có phân trang chuẩn.

### 3. Cảnh báo xe đi lệch lộ trình (Route Deviation Alert)
- Khi nhận tọa độ mới của xe đang trong chuyến thuê có lộ trình đăng ký/dự kiến (qua Mapbox Directions polyline precision5):
  - Tính toán khoảng cách vuông góc nhỏ nhất từ vị trí xe tới các đoạn thẳng của đường đi dự kiến.
  - Nếu khoảng cách vượt ngưỡng cho phép (cấu hình mặc định 500m), hệ thống kích hoạt cảnh báo `route_deviation` mức cảnh báo (Warning).
  - Ghi nhận thông tin chi tiết: tọa độ hiện tại, tọa độ dự kiến gần nhất, khoảng cách lệch (mét).

### 4. Cảnh báo trả sai điểm (Wrong Return Point Alert)
- Khi hoàn tất thuê hoặc kiểm tra trả xe (`POST /api/tracking/rentals/{rentalId}/verify-dropoff`):
  - Xác định trạm nhận/trả xe quy định của đơn vị cho thuê (tọa độ của `facility` thuộc hợp đồng thuê, hoặc địa chỉ nhận xe ban đầu).
  - So sánh tọa độ hiện tại của xe với tọa độ trạm trả xe quy định.
  - Nếu khoảng cách thực tế vượt quá dung sai cho phép (cấu hình mặc định 250m - 500m):
    - Đánh dấu trạng thái trả sai điểm (`isValid = false`).
    - Tự động tạo cảnh báo `wrong_return_point` trong bảng `tracking_alerts`.
    - Thông báo cho nhân viên điều phối để hỗ trợ xử lý thu hồi/thu phụ phí di chuyển xe về đúng trạm.
- API quản lý cảnh báo: `GET /api/tracking/alerts` (có phân trang) và `POST /api/tracking/alerts/{alertId}/resolve` để nhân viên đóng cảnh báo sau khi đã xử lý.

## Acceptance criteria

- [x] Lộ trình thuê tổng hợp chính xác quãng đường, thời gian, vận tốc trung bình, vận tốc tối đa và mảng tọa độ.
- [x] Geofencing phát hiện chuyển đổi trạng thái ENTER/EXIT và lưu trữ lịch sử sự kiện.
- [x] Tự động tạo cảnh báo khi xe rời vùng được phép hoặc đi vào vùng cấm.
- [x] Thuật toán đo khoảng cách tới polyline phát hiện lệch lộ trình thuê khi vượt ngưỡng cho phép.
- [x] API kiểm tra trả xe phát hiện trả sai điểm khi lệch khỏi trạm quy định và sinh cảnh báo cho nhân viên.
- [x] Nhân viên có thể tra cứu và đánh dấu đã xử lý các cảnh báo.
- [x] Phân trang đầy đủ cho danh sách geofences, geofence events và alerts.
- [x] Response contract 6 trường, message đúng ngữ cảnh nghiệp vụ.
