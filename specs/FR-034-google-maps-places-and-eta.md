# FR-034 — Tích hợp Google Maps API: Places, Geocoding, tìm xe theo bán kính, khoảng cách và ETA

- **Status:** Done; **Priority:** P1; **Milestone:** M2
- **Change:** [2026-10-10-gps-tracking-and-google-maps.md](../changes/2026-10-10-gps-tracking-and-google-maps.md)

## Nghiệp vụ và luồng hoạt động

### 1. Tích hợp Places & Geocoding
- Kết nối với Google Maps Platform qua `HttpClient` được cấu hình trong tầng Infrastructure (`GoogleMapsClient`).
- Cấu hình qua `GoogleMapsOptions`: `ApiKey`, `BaseUrl`, `Enabled`. Khi không có API key hoặc ở môi trường test/dev ngoại tuyến, hệ thống tự động fallback thuật toán nội bộ/ước lượng an toàn mà không làm sập ứng dụng.
- **Forward Geocoding**: `GET /api/map/geocode?address=...` chuyển đổi địa chỉ thành tọa độ (lat, lng, formatted_address, place_id).
- **Reverse Geocoding**: `GET /api/map/reverse-geocode?lat=...&lng=...` chuyển đổi tọa độ thành địa chỉ đọc được.
- **Places Search**: `GET /api/map/places?query=...&lat=...&lng=...&radius=...` tìm kiếm địa điểm, trạm dịch vụ, điểm đón trả khách.

### 2. Khoảng cách và ETA (Distance Matrix & Directions)
- `GET /api/map/distance-matrix?originLat=...&originLng=...&destLat=...&destLng=...`: Gọi Google Maps Distance Matrix API để lấy khoảng cách đường bộ thực tế (mét) và thời gian di chuyển dự kiến (giây/phút) có tính đến giao thông.
- `GET /api/map/directions?originLat=...&originLng=...&destLat=...&destLng=...`: Gọi Google Maps Directions API để lấy lộ trình chỉ đường chi tiết, bao gồm polyline đã mã hóa và các điểm mốc (waypoints).

### 3. Truy vấn xe theo bán kính (Radius Vehicle Search)
- `GET /api/tracking/vehicles/nearby?lat=...&lng=...&radiusMeters=...&includeEta=true&page=1&pageSize=20`:
  - Khách hàng hoặc điều phối viên tìm kiếm các xe gần vị trí mong muốn trong bán kính chỉ định (mặc định 5.000m).
  - Lọc nhanh các xe có vị trí gần nhất trong bán kính bằng công thức Haversine (tính trực tiếp trên SQL hoặc spatial filter).
  - Khi `includeEta=true`, hệ thống tích hợp gọi Google Maps Distance Matrix để tính chính xác khoảng cách đường lái xe và ETA cho từng xe trong danh sách trang hiện tại.
  - Phân trang đầy đủ: `items`, `page`, `pageSize`, `totalCount`.

## Acceptance criteria

- [x] Có adapter kết nối Google Maps Geocoding, Places, Distance Matrix và Directions API với cơ chế fallback khi offline.
- [x] Endpoint Geocode và Reverse Geocode chuyển đổi chính xác địa chỉ và tọa độ.
- [x] Endpoint Distance Matrix và Directions tính khoảng cách và ETA.
- [x] API tìm kiếm xe theo bán kính hỗ trợ phân trang và tính khoảng cách/ETA.
- [x] Phân trang tuân thủ `page=1`, `pageSize=20`, `1 <= pageSize <= 100`, validation tại HTTP và Application.
- [x] Response contract 6 trường, message đúng ngữ cảnh nghiệp vụ.
