# Mapbox — cấu hình và kiểm chứng

Từ 2026-10-20, bản đồ dùng Mapbox thay Google Maps. Google OAuth không thay đổi. Adapter `MapboxClient` thực hiện port `IMapClient`; cấu hình `GoogleMaps__*` cũ không còn được đọc.

## Token và backend Railway

Tạo hai **public access tokens `pk.`** tại [Mapbox](https://account.mapbox.com/access-tokens/). Token trình duyệt giới hạn theo URL frontend/localhost, giữ public scopes mặc định cho GL JS (`styles:read`, `styles:tiles`, `fonts:read`). Token backend dùng riêng, không URL restriction của website vì request không có browser Referer. Không cần secret token `sk.`.

Trên service **backend → Variables**:

```env
Mapbox__Enabled=true
Mapbox__AccessToken=pk.YOUR_SERVER_PUBLIC_TOKEN
Mapbox__DrivingProfile=driving
Mapbox__PermanentGeocoding=true
```

Các giá trị trên là placeholder. Token thật lưu ngoài Git. Local có thể dùng `dotnet user-secrets` cho project WebApi (nếu có UserSecretsId), biến môi trường của tiến trình hoặc section `Mapbox` trong appsettings local đã được gitignore:

```json
"Mapbox": {
  "Enabled": true,
  "AccessToken": "pk.YOUR_SERVER_PUBLIC_TOKEN",
  "DrivingProfile": "driving",
  "PermanentGeocoding": true,
  "DefaultSearchRadiusMeters": 5000,
  "RouteDeviationThresholdMeters": 500,
  "ReturnLocationToleranceMeters": 250
}
```

Backend không tự đọc file `.env`. Phải cấp biến vào tiến trình .NET hoặc appsettings. Khi Enabled=true mà không có public token, startup báo lỗi cấu hình. Mặc định chưa cấu hình thì API Maps trả 503; GPS/nearby không cần ETA vẫn hoạt động.

Geocoding backend mặc định `permanent=true` vì tọa độ có thể được lưu trong cảnh báo xác minh trả xe. Mapbox yêu cầu tài khoản đủ điều kiện permanent (thẻ hợp lệ hoặc hợp đồng enterprise); xem [Geocoding — storage](https://docs.mapbox.com/api/search/geocoding/#storing-geocoding-results). Không tắt permanent nếu đang lưu kết quả geocoding vào DB. Tìm địa chỉ `/places` dùng temporary và không cache/lưu ở backend.

## Frontend

Trong `.env.local` tại gốc repo FE, hoặc môi trường **build** frontend:

```env
NEXT_PUBLIC_MAPBOX_ACCESS_TOKEN=pk.YOUR_BROWSER_PUBLIC_TOKEN
```

Thêm localhost và domain triển khai vào allowed URLs của token trình duyệt theo [hướng dẫn bảo vệ token](https://docs.mapbox.com/help/dive-deeper/how-to-use-mapbox-securely/). Khởi động lại dev server; static export cần build/deploy lại. Chỉ thêm biến ở backend hoặc Worker runtime sẽ không cập nhật JavaScript FE đã build. Xem [hướng dẫn FE](../../Car-Rental-System-FE/docs/mapbox-frontend.md).

## API và giới hạn

- Giữ URL `/api/map/*`, quyền đăng nhập và envelope 6 trường. `/places` đổi từ array sang `data.items/page/pageSize/totalCount`, mặc định 1/20, pageSize tối đa100.
- Việt Nam dùng [Geocoding v6](https://docs.mapbox.com/api/search/geocoding/). [Search Box POI](https://docs.mapbox.com/api/search/search-box/#language-and-geography-support) chưa hỗ trợ Việt Nam; `/places` chỉ tìm địa chỉ. Upstream trả tối đa10 matches, pagination áp dụng trong tập đó. Bán kính lọc sau tìm kiếm, không đảm bảo liệt kê toàn bộ địa điểm trong bán kính.
- [Matrix](https://docs.mapbox.com/api/navigation/matrix/) trả mét/giây; [Directions](https://docs.mapbox.com/api/navigation/directions/) dùng encoded polyline precision5. Thứ tự request Mapbox là longitude,latitude; DTO ứng dụng vẫn Lat/Lng.
- `driving` không cam kết giao thông live. Chỉ chuyển `driving-traffic` sau khi xác minh coverage/tài khoản.
- Thiếu cấu hình 503; provider lỗi 502/503; không tìm địa chỉ/tuyến 404. Nearby để road distance/ETA null khi provider không khả dụng; không sinh dữ liệu mô phỏng.
- Xác minh trả xe thiếu tọa độ cơ sở và không geocode được trả 422; không tạo cảnh báo dựa vào vị trí giả.
- HttpClient Mapbox tắt request logging để token query không vào log; thông báo lỗi không chứa URL/token/response body.

## Kiểm tra sau cấp token

1. Đăng nhập rồi gọi `/api/map/geocode?address=12%20Nguy%E1%BB%85n%20Hu%E1%BB%87%2C%20TP.HCM`; đối chiếu lat/lng với địa chỉ thật, reverse geocode lại.
2. Gọi `/api/map/places?query=Nguyen%20Hue&page=1&pageSize=1`; kiểm tra `totalCount` trong tập matches, thử page0/pageSize101 →400.
3. Gọi matrix/directions cho hai tọa độ thật; kiểm tra mét/giây, route không phải đường thẳng tự tạo. `EncodedPolyline` vẫn dùng precision5.
4. Mở chi nhánh trên FE, đổi chi nhánh, thử mobile, bấm chỉ đường và cấp/từ chối quyền định vị.
5. Thử token sai, thiếu token và mất mạng: có lỗi/thử lại, không có tọa độ/ETA giả. Theo dõi usage trong Mapbox Console.

Tests dùng HTTP fixtures/mock SDK, không gọi API tính phí. Chưa có token thật thì chưa xác nhận coverage, billing hay kết quả trên deployment.
