# Thay Google Maps bằng Mapbox — 2026-10-20

## Thay đổi

- `IMapClient` thay port gắn tên Google, `MapboxClient` dùng Geocoding v6, Matrix v1, Directions v5; section `Mapbox`, public token riêng cho server. Timeout15s, validate cấu hình lúc startup, tắt HttpClient URL logging để không lộ token.
- Giữ URL/quyền/envelope Maps; `/places` chuyển sang `PlaceSearchPage` với validation HTTP/Application, cặp lat/lng, radius và offset an toàn. Upstream tối đa10 matches; tại Việt Nam chỉ address search do Search Box POI chưa hỗ trợ.
- Không trả tọa độ/ETA/polyline mô phỏng. Missing config503, upstream502/503, no-route404. Nearby giữ Haversine nhưng road distance/ETA null khi Mapbox lỗi; không nuốt cancellation.
- Xác minh trả xe không dùng vị trí tâm thành phố giả. Thiếu vị trí cơ sở và không geocode được trả422. Permanent geocoding mặc định cho kết quả có thể lưu trong cảnh báo; cần tài khoản Mapbox đủ điều kiện.
- FE dùng Mapbox GL JS cho toàn bộ vị trí hiện có và chỉ đường ngay trong bản đồ; cập nhật spec FR-034/FR-035, API/Swagger, hướng dẫn và link tài liệu cũ.

## Kiểm chứng

- Backend build `-warnaserror -m:1 /p:BuildInParallel=false /nodeReuse:false`: đạt, 0 warnings/errors.
- 84 tests Mapbox/Maps HTTP/Tracking/API response: đạt; fixture HTTP kiểm tra contract đủ6 trường, phân quyền, pagination, upstream errors, malformed data, no-route, tọa độ/polyline, cancellation, nearby và cơ sở thiếu vị trí.
- FE typecheck/ESLint/design lint đạt; 512 unit tests đạt; 10 tests Mapbox chạy lại sau chỉnh thông báo lỗi đạt; production static export webpack đạt (258 trang).
- 6/6 E2E đạt trên static export với token giả và mọi provider request được mock (Chrome cài sẵn, server riêng cổng3100). Chưa kiểm chứng token thật, billing/coverage hoặc triển khai.

## Cấu hình cần cấp

Backend: `Mapbox__Enabled=true`, `Mapbox__AccessToken=pk...`; frontend build: `NEXT_PUBLIC_MAPBOX_ACCESS_TOKEN=pk...` từ token riêng có URL restrictions. Xem [setup](../docs/mapbox-setup.md). Token thật không có trong thay đổi này; chưa deploy.
