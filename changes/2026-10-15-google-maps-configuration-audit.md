# 2026-10-15 — Kiểm tra tích hợp Google Maps và cấu hình Railway

## Kết quả

- Backend có adapter, DI, endpoint và tracking consumer; cả hai appsettings chưa khai báo `GoogleMaps`, mặc định `Enabled=false` và key rỗng nếu không được cấp từ nguồn cấu hình khác.
- Frontend cùng dự án chưa nối SDK hoặc API Maps/Tracking.
- Adapter đang dùng Places, Directions, Distance Matrix Legacy; ghi rõ giới hạn project mới và phân biệt với Places API (New)/Routes API.
- Thêm [docs/google-maps-setup.md](../docs/google-maps-setup.md): mẫu JSON không chứa secret, biến môi trường Railway, API/IP restrictions, kiểm tra fallback và giới hạn ETA hiện tại. Cập nhật FR-034/progress để phân biệt triển khai mã với kiểm chứng dịch vụ thật.

## Kiểm chứng

Đọc options, Program, GoogleMapsClient, MapController, TrackingService, appsettings, railway.toml và tìm references/dependencies ở frontend. Đối chiếu tài liệu chính thức Google/Railway/Microsoft được liên kết trong hướng dẫn. Chỉ sửa tài liệu; không chạy lại build/test, không gọi Google tính phí, không truy cập hoặc sửa cấu hình Railway/Google Cloud, không ghi key thật.
