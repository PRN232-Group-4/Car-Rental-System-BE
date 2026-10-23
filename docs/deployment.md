# Deploy Railway

Railway dùng `Dockerfile` và probe `/health` theo `railway.toml`. Tạo service API, PostgreSQL và Redis; đặt các biến `DATABASE_URL` (hoặc `ConnectionStrings__RentalDatabase`), `REDIS_URL` (hoặc `Redis__ConnectionString`), `Brevo__ApiKey`, `Brevo__SenderEmail`, `Brevo__SenderName`, `ASPNETCORE_ENVIRONMENT=Production`. Railway cấp `PORT`; Docker entrypoint bind đúng port đó.

Trong **Settings → Networking → Public Networking**, domain phải trỏ tới **Target port `8080`**. Log phải có `Now listening on: http://[::]:8080`; nếu domain trả 502 dù có dòng này thì target port đang sai hoặc domain thuộc service/deployment cũ.

Chỉ phát hành mã đã được review/merge theo quy trình PR; không push trực tiếp main. `railway.toml` chọn Dockerfile và healthcheck, nhưng workflow hiện tại chỉ build/test, chưa có bước tự deploy Railway. Xác nhận service/nhánh liên kết trên Railway trước khi phát hành; rollback qua deployment trước đó. Migration runner thực tế chạy khi app khởi động nếu `Database__ApplyMigrations=true` (mặc định true), áp dụng các file SQL theo thứ tự và kiểm tra checksum. Kiểm tra backup/staging và quyền migrate trước phát hành; nếu môi trường chủ động tắt runner, cần áp dụng migration có kiểm soát trước khi chạy mã mới.

### API xác nhận inspect — 2026-10-22

FE hiện proxy tới Railway theo `next.config.ts`. Nếu xác nhận đạt trả 404, kiểm tra Swagger của **chính origin đó** có POST `/api/rentals/{id}/inspection-assignment/checked` không. Bản source mới có route Admin này và cần migration `0028_driver_inspection_confirmation.sql`. Cập nhật FE hoặc khởi động lại một backend local khác không cập nhật service Railway. Sau phát hành, xác minh healthcheck, route Swagger và migration thành công; không gọi inspect/cancel trên đơn thật để thử nếu chưa có yêu cầu nghiệp vụ.

## Reverse proxy Caddy khi chạy bằng docker compose

Compose có thêm service `caddy` đứng trước `api`. Cấu hình site nằm trong `Caddyfile` ở gốc repo và được mount vào container theo đúng yêu cầu triển khai:

```yaml
  caddy:
    image: caddy:2-alpine
    volumes:
      - ./Caddyfile:/etc/caddy/Caddyfile
      - caddy_data:/data
      - caddy_config:/config
    ports:
      - "${CADDY_HTTP_PORT:-80}:80"
      - "${CADDY_HTTPS_PORT:-443}:443"
    environment:
      CADDY_SITE_ADDRESS: "${CADDY_SITE_ADDRESS:-:80}"
```

- Mặc định `CADDY_SITE_ADDRESS=:80`: Caddy phục vụ HTTP cho mọi hostname và `reverse_proxy api:8080`. Cổng nội bộ 8080 lấy từ `Dockerfile` (`ASPNETCORE_URLS=http://+:8080`), không phải cổng `3000` đang publish ra host cho FE dev.
- Đặt tên miền thật để Caddy tự xin/gia hạn chứng chỉ HTTPS: `CADDY_SITE_ADDRESS=api.example.com docker compose up -d --build caddy`. Lúc đó DNS của tên miền phải trỏ về máy này và cổng 80/443 phải mở; chứng chỉ lưu trong volume `caddy_data` nên restart container không xin lại (tránh giới hạn tần suất của Let's Encrypt).
- Đổi cổng publish khi 80/443 đã bị chiếm trên máy chủ: `CADDY_HTTP_PORT=8080 CADDY_HTTPS_PORT=8443 docker compose up -d caddy`.
- Caddy tự gửi `X-Forwarded-For`/`Proto`/`Host`; API đã bật `UseForwardedHeaders` với `KnownNetworks`/`KnownProxies` được xoá nên nhận đúng scheme (cookie `Secure`, `redirect_uri` Google OAuth) và IP thật của khách.
- Compose chỉ dùng cho môi trường tự vận hành/local. Bản Railway vẫn build `Dockerfile` theo `railway.toml` và không dùng Caddy này.
- Kiểm tra nhanh: `docker compose exec -T caddy ls -l /etc/caddy/` (phải là file `Caddyfile`, không phải thư mục), `docker compose logs caddy` phải có dòng `using config from file` và `server running`; gọi `http://localhost/health` khi `api` chưa chạy sẽ trả `502` (đúng vì proxy đang hoạt động mà upstream chưa lên).

## Chạy và xem connection log bằng CLI

Khởi động dependencies local: `docker compose up db redis -d`. Trong PowerShell, đặt `Jwt__SigningKey` (ít nhất 32 ký tự ngẫu nhiên), `Brevo__ApiKey`, `Brevo__SenderEmail`, rồi chạy `dotnet run --project src\VehicleRental.WebApi\VehicleRental.WebApi.csproj`. Ở Development, API mặc định kết nối PostgreSQL `localhost:5432` và Redis `localhost:6379` theo cấu hình Compose; các service này vẫn phải được khởi động trước. Swagger có tại `http://localhost:3000/swagger`. Console phải có `Redis connection established`; khi lỗi sẽ in tên biến cấu hình cần kiểm tra và stack trace, không in secret. Dừng/xem log dependencies: `docker compose logs -f db redis`.

`Redis:ConnectionString=localhost:6379` chỉ là địa chỉ, không tự khởi động Redis. Với local Docker chạy `docker compose up redis -d`; kiểm tra bằng `docker compose ps redis`. Nếu dùng Redis cloud, thay bằng endpoint/password/TLS do nhà cung cấp cấp.

API nhận cả URI cloud (`redis://user:password@host:port` hoặc TLS `rediss://...`) lẫn format StackExchange.Redis (`host:port,password=...`). Neon URI `postgresql://...` cũng được đổi sang Npgsql connection string khi startup. Secrets không được để trong `appsettings*.json` đã tracked; dùng environment variables hoặc user-secrets.

## Timeout khi Npgsql nạp metadata

Nếu stack trace dừng ở `PostgresDatabaseInfo.LoadBackendTypes` trong `OpenConnectionAsync`, SQL migration chưa bắt đầu. Kiểm tra kết nối và truy vấn chỉ đọc trước khi sửa migration. Tăng timeout không giải quyết được trường hợp kết nối bị đóng trong lúc nạp metadata.

Workaround cho môi trường chỉ dùng kiểu PostgreSQL tích hợp: đặt `Database__SkipTypeLoading=true` hoặc `Database:SkipTypeLoading=true` trong cấu hình môi trường. Npgsql 8 sẽ dùng `Server Compatibility Mode=NoTypeLoading`. Mặc định cờ tắt; migration vẫn hoạt động theo `Database:ApplyMigrations`. Không dùng workaround nếu cần enum/domain/composite tự định nghĩa; tắt cờ để khôi phục nạp metadata đầy đủ. Xem [Npgsql](https://www.npgsql.org/doc/api/Npgsql.ServerCompatibilityMode.html). Cờ không chứa secret và không yêu cầu sao chép connection string.

## Google OAuth và Cloudinary

On Railway, also set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`, and keep the exact Google redirect URI `https://<railway-domain>/signin-google`. The API consumes Railway's forwarded HTTPS and host headers before running authentication.

If the callback fails, the API returns a safe `400` response and writes `Google OAuth callback failed.` with the underlying exception to the Railway deployment log. Check that migration `0008_staff_authorization` has run and that database and Redis connectivity is healthy; do not log or share any client secret, cookie, or connection string.

Đặt Railway variables: `GoogleAuthentication__Enabled=true`, `GoogleAuthentication__ClientId`, `GoogleAuthentication__ClientSecret`, `GoogleAuthentication__SuccessRedirectUrl=https://<frontend-domain>`, `Cloudinary__CloudName`, `Cloudinary__ApiKey`, `Cloudinary__ApiSecret`. Trong Google Cloud Console thêm Authorized redirect URI `https://<railway-domain>/signin-google`; local là `http://localhost:3000/signin-google`. Khi callback hoàn tất, API ghi cookie đăng nhập rồi redirect về `GoogleAuthentication__SuccessRedirectUrl`. Không dùng `appsettings.json` cho các giá trị bí mật này.
