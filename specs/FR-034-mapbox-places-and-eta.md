# FR-034 — Mapbox: địa chỉ, tìm xe theo bán kính, khoảng cách và ETA

- **Status:** Implemented; pending live token verification; **Priority:** P1; **Milestone:** M2
- **Change:** [2026-10-20-mapbox-migration.md](../changes/2026-10-20-mapbox-migration.md)

## Phạm vi

Thay Google Maps bằng Mapbox ở FE/BE. Port `IMapClient` độc lập nhà cung cấp; adapter Infrastructure dùng Geocoding v6, Matrix v1 và Directions v5. `Mapbox` là section cấu hình duy nhất cho bản đồ. Không đổi Google OAuth.

- Giữ các route `/api/map/geocode`, `reverse-geocode`, `places`, `distance-matrix`, `directions` và quyền đăng nhập. PlaceId là ID Mapbox, không phải Google Place ID.
- Việt Nam dùng Geocoding v6 để tìm địa chỉ. `/places` không cam kết POI như Google Places: Search Box hiện chỉ hỗ trợ Mỹ, Canada, châu Âu. Giới hạn upstream 10 kết quả; phân trang `page=1`, `pageSize=20` (1..100) trên tập kết quả đó, không phải tổng địa điểm toàn cầu.
- `/places` trả `items/page/pageSize/totalCount`, validate tại HTTP và Application; cặp lat/lng phải đầy đủ, radius cần tâm, lọc khoảng cách Haversine sau tìm kiếm.
- Mapbox nhận thứ tự `longitude,latitude`; response ứng dụng giữ Lat/Lng và mét/giây. Directions yêu cầu polyline precision 5 để tương thích dữ liệu geofence hiện tại.
- Profile mặc định `driving`; ETA không được quảng cáo là giao thông thời gian thực. Có thể cấu hình `driving-traffic` nếu tài khoản/khu vực hỗ trợ.
- Thiếu cấu hình trả 503, lỗi upstream 502/503, không tìm tuyến trả 404. Không tạo tọa độ, polyline hoặc ETA giả. Nearby vẫn trả khoảng cách Haversine từ DB; ETA/road distance để null nếu Mapbox không khả dụng; cancellation phải được truyền tiếp.
- Xác minh trả xe chỉ dùng tọa độ cơ sở thật hoặc geocoding thành công; thiếu vị trí đích thì dừng, không xác minh với tâm thành phố mặc định.
- Token nằm ngoài Git; không log URL chứa access_token. Backend geocode dùng permanent mặc định vì kết quả có thể được lưu trong cảnh báo trả xe; cần tài khoản Mapbox đủ điều kiện permanent. FE geocode tạm thời không lưu vào DB/cache.
- FE dùng Mapbox GL JS tải theo nhu cầu, marker thật, chỉ đường trong bản đồ và trạng thái loading/error/retry; GPS chỉ lấy khi người dùng mở panel, không tự bật định vị.

## Acceptance criteria

- [x] Adapter tests xác minh endpoint, tọa độ, mapping, polyline, null/no-route, lỗi HTTP/JSON, missing token, cancellation.
- [x] `/places` và lỗi Mapbox đúng envelope 6 trường; Swagger mô tả schema và pagination.
- [x] FE kiểm tra token thiếu, tọa độ 0/invalid, địa chỉ tiếng Việt, lỗi API, hủy request và chỉ đường.
- [x] Build/typecheck và test liên quan đạt; gọi Mapbox thật chỉ được xác nhận khi có token hoạt động.
