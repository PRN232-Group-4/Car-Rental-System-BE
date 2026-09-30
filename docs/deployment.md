# Deploy Railway

Railway dùng `Dockerfile` và probe `/health` theo `railway.toml`. Tạo service API, PostgreSQL và Redis; đặt các biến `DATABASE_URL` (hoặc `ConnectionStrings__RentalDatabase`), `REDIS_URL` (hoặc `Redis__ConnectionString`), `Brevo__ApiKey`, `Brevo__SenderEmail`, `Brevo__SenderName`, `ASPNETCORE_ENVIRONMENT=Production`. Railway cấp `PORT`; Docker entrypoint bind đúng port đó.

Trong **Settings → Networking → Public Networking**, domain phải trỏ tới **Target port `8080`**. Log phải có `Now listening on: http://[::]:8080`; nếu domain trả 502 dù có dòng này thì target port đang sai hoặc domain thuộc service/deployment cũ.

GitHub secret: `RAILWAY_TOKEN`; repository variable: `RAILWAY_SERVICE_NAME`. Release qua push `main`; rollback bằng Railway deployment trước đó hoặc redeploy commit trước. Migration không chạy lúc app khởi động; tạo hoặc cập nhật schema PostgreSQL có kiểm soát trước khi deploy.

## Chạy và xem connection log bằng CLI

Khởi động dependencies local: `docker compose up db redis -d`. Trong PowerShell, đặt `Brevo__ApiKey`, `Brevo__SenderEmail`, rồi chạy `dotnet run`. Console phải có `Redis connection established`; khi lỗi sẽ in tên biến cấu hình cần kiểm tra và stack trace, không in secret. Dừng/xem log dependencies: `docker compose logs -f db redis`.

`Redis:ConnectionString=localhost:6379` chỉ là địa chỉ, không tự khởi động Redis. Với local Docker chạy `docker compose up redis -d`; kiểm tra bằng `docker compose ps redis`. Nếu dùng Redis cloud, thay bằng endpoint/password/TLS do nhà cung cấp cấp.

API nhận cả URI cloud (`redis://user:password@host:port` hoặc TLS `rediss://...`) lẫn format StackExchange.Redis (`host:port,password=...`). Neon URI `postgresql://...` cũng được đổi sang Npgsql connection string khi startup. Secrets không được để trong `appsettings*.json` đã tracked; dùng environment variables hoặc user-secrets.

## Google OAuth và Cloudinary

Đặt Railway variables: `GoogleAuthentication__Enabled=true`, `GoogleAuthentication__ClientId`, `GoogleAuthentication__ClientSecret`, `Cloudinary__CloudName`, `Cloudinary__ApiKey`, `Cloudinary__ApiSecret`. Trong Google Cloud Console thêm Authorized redirect URI `https://<railway-domain>/signin-google`; local là `http://localhost:3000/signin-google`. Không dùng `appsettings.json` cho các giá trị này.
