# Google Maps: trạng thái tích hợp và cấu hình

Kiểm tra mã nguồn ngày 2026-10-15. Chưa kiểm tra Google Cloud hoặc Railway đang chạy.

## Đã tích hợp đến đâu?

- Backend đã có adapter `GoogleMapsClient`, `MapService`, `TrackingService` và đăng ký DI trong `src/VehicleRental.WebApi/Program.cs`.
- Các API cần đăng nhập: `GET /api/map/geocode`, `/reverse-geocode`, `/places`, `/distance-matrix`, `/directions` (cùng tiền tố `/api/map`). Xem [API.md](API.md#tích-hợp-google-maps-places-geocoding-distance-matrix-directions).
- `GET /api/tracking/vehicles/nearby?lat=...&lng=...&includeEta=true` gọi Distance Matrix cho từng xe của trang hiện tại. Kiểm tra điểm trả xe dùng Geocoding khi chi nhánh chưa có tọa độ.
- Hai file `src/VehicleRental.WebApi/appsettings.json` và `appsettings.Development.json` hiện không có section `GoogleMaps`. Nếu không được biến môi trường/user secrets ghi đè, options mặc định là `Enabled=false`, `ApiKey=""`: backend trả dữ liệu mô phỏng/ước lượng.
- Frontend cùng thư mục dự án (`Car-Rental-System-FE`): chưa tìm thấy SDK bản đồ, biến Maps, lời gọi `/api/map/*` hoặc `/api/tracking/*` trong `src/`, hay dependency Maps trong `package.json`. Thêm key backend chưa tự tạo bản đồ trên giao diện.

Nguồn mã: [options](../src/VehicleRental.Application/DTOs/Options/GoogleMapsOptions.cs), [adapter](../src/VehicleRental.Infrastructure/Services/GoogleMapsClient.cs), [controller](../src/VehicleRental.WebApi/Controllers/MapController.cs), [tracking](../src/VehicleRental.Application/Services/TrackingService.cs).

## Cấu hình appsettings và chạy local

Section mà chương trình đọc là `GoogleMaps`. Mẫu có thể thêm vào `appsettings.json` hoặc `appsettings.Development.json`:

```json
"GoogleMaps": {
  "Enabled": false,
  "ApiKey": "",
  "BaseUrl": "https://maps.googleapis.com/maps/api/",
  "DefaultSearchRadiusMeters": 5000,
  "RouteDeviationThresholdMeters": 500,
  "ReturnLocationToleranceMeters": 250
}
```

Đây là thuộc tính trong object JSON gốc, cần ghép dấu phẩy với các section đang có. Giữ key thật ngoài Git. Để gọi Google thật, cấp `GoogleMaps__Enabled=true` và `GoogleMaps__ApiKey=<BACKEND_MAPS_API_KEY>` qua biến môi trường của tiến trình chạy WebApi; các giá trị còn lại có thể giữ mặc định. Chỉ đặt key mà thiếu `Enabled=true` vẫn dùng fallback.

ASP.NET Core chuyển `__` thành `:` và biến môi trường ghi đè appsettings. Mã hiện không tự đọc file `.env`; chỉ tạo `.env` sẽ chưa nạp cấu hình. Xem [tài liệu cấu hình Microsoft](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/?view=aspnetcore-8.0#environment-variables).

## Railway

Chọn đúng environment và **service backend** → **Variables** → **RAW Editor**, thêm:

```dotenv
GoogleMaps__Enabled=true
GoogleMaps__ApiKey=<BACKEND_MAPS_API_KEY>
GoogleMaps__BaseUrl=https://maps.googleapis.com/maps/api/
```

`BaseUrl` là tùy chọn; giữ dấu `/` cuối đường dẫn nếu khai báo. Thay placeholder bằng key backend trong Railway. Các ngưỡng tùy chọn dùng cùng quy tắc: `GoogleMaps__DefaultSearchRadiusMeters`, `GoogleMaps__RouteDeviationThresholdMeters`, `GoogleMaps__ReturnLocationToleranceMeters`.

Review staged changes và **Deploy** để tiến trình nhận biến mới. `railway.toml` hiện chỉ chứa build Dockerfile, healthcheck và restart policy; key nằm trong Variables của service. Không cần biến `NEXT_PUBLIC_GOOGLE_MAPS_API_KEY` cho frontend hiện tại. Xem [Railway Variables](https://docs.railway.com/variables).

## Google Cloud và phiên bản API

Adapter hiện gọi:

| Đường dẫn Google | API cần được cấp quyền |
| --- | --- |
| `geocode/json` | Geocoding API |
| `place/textsearch/json` | Places API (Legacy) |
| `distancematrix/json` | Distance Matrix API (Legacy) |
| `directions/json` | Directions API (Legacy) |

Cần Google Cloud project có billing, bật các API tương ứng và tạo key. Places, Directions và Distance Matrix trên đây đã ở trạng thái Legacy từ 2025-03-01; Google không cung cấp mặc định cho project mới. Project đang dùng Legacy có thể tiếp tục, và Google mô tả phương án cấp quyền tạm thời trong [tài liệu Legacy](https://developers.google.com/maps/legacy). Nếu project mới chỉ bật **Places API (New)** và **Routes API**, phải chuyển adapter sang API mới trước; đổi key hoặc `BaseUrl` đơn thuần không chuyển đổi request/response.

Với key backend, giới hạn API đúng bảng trên và application restriction bằng **IP outbound** của backend. Domain Railway/frontend dùng cho website referrer không thay thế IP của lời gọi server. Railway Pro hỗ trợ **Settings → Networking → Enable Static IPs**; cho phép toàn bộ IPv4 outbound hiển thị theo region và redeploy. Đây không phải IP inbound/domain; IP có thể đổi khi đổi region. Nếu gói hiện tại không có IP outbound cố định, cần phương án egress cố định phù hợp trước khi dùng IP allowlist. Xem [Google key restrictions](https://developers.google.com/maps/api-security-best-practices) và [Railway Static Outbound IPs](https://docs.railway.com/networking/static-outbound-ips).

Nếu sau này thêm Maps JavaScript API trên giao diện, tạo key trình duyệt riêng với website referrer restrictions; không đưa key backend vào biến `NEXT_PUBLIC_*`.

## Xác nhận đang dùng Google thật

1. Đăng nhập ứng dụng, gọi `GET /api/map/distance-matrix?originLat=10.7769&originLng=106.7009&destLat=10.7626&destLng=106.6602` bằng session/token hợp lệ.
2. Kiểm tra `data.status`: `OK` là kết quả Google; `OK_FALLBACK` là chưa cấu hình; `FALLBACK` là đã cấu hình nhưng không lấy được kết quả. HTTP 200 tự nó chưa chứng minh Google hoạt động.
3. Geocode trả `placeId="fallback_place_id"` hoặc Places trả `placeId="fallback_1"` là dữ liệu mô phỏng. Directions fallback có polyline rỗng. Đối chiếu usage/errors của API trong Google Cloud để kiểm tra key/billing/quota/API restrictions.

Lưu ý adapter đang fallback cả khi provider lỗi; API có thể trả thành công dù key/API không hợp lệ. Distance Matrix hiện đọc `duration`, không truyền `departure_time` và không đọc `duration_in_traffic`; chưa xác nhận ETA theo giao thông trực tiếp. Không đưa URL request chứa key vào ảnh chụp, log được chia sẻ hoặc Git.
