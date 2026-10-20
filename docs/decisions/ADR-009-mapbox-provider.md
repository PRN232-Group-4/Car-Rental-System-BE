# ADR-009 — Mapbox làm nhà cung cấp bản đồ

- **Ngày:** 2026-10-20
- **Trạng thái:** Accepted theo yêu cầu chuyển toàn bộ Google Maps của người dùng; chờ kiểm chứng token/deployment thật.

## Bối cảnh

FE dùng Google Maps Embed, BE dùng Google Geocoding/Places/Matrix/Directions và tạo kết quả mô phỏng khi lỗi. Cần thay provider đồng bộ, giữ contract GPS/geofence và loại bỏ dữ liệu có thể gây hiểu nhầm khi vận hành.

## Quyết định

- Application dùng `IMapClient`; Infrastructure triển khai `MapboxClient`. Cấu hình `Mapbox` với token public `pk.` riêng cho backend. FE dùng public token khác, giới hạn theo URL, và Mapbox GL JS import động.
- Dùng Geocoding v6, Matrix v1, Directions v5. Giữ DTO Lat/Lng, đơn vị mét/giây và encoded polyline precision5. Không thay Google OAuth.
- `/places` dùng address search ở Việt Nam vì Search Box POI chưa có coverage; phân trang chuẩn trên tối đa10 provider matches. Không tuyên bố số lượng này là tổng địa điểm toàn cầu.
- Không tạo tọa độ/ETA/route giả. API báo provider error hoặc no-route; nearby giữ Haversine và nullable road distance/ETA. Thiếu vị trí cơ sở không được xác minh trả xe với vị trí mặc định.
- Geocode backend mặc định permanent vì kết quả có thể lưu trong cảnh báo. Geocode FE và address search tạm thời không lưu/cache. Giữ attribution SDK và tắt URL logging trên HttpClient provider.

## Hệ quả

`/places` chuyển từ array sang `items/page/pageSize/totalCount`; consumer phải cập nhật khi triển khai. `PlaceId` nay là Mapbox ID. FE static export cần rebuild khi đổi token; service BE cần biến Mapbox và tài khoản đủ điều kiện permanent. Cần kiểm chứng coverage/billing/URL restrictions với token thật trước phát hành. Xem [setup](../mapbox-setup.md).
