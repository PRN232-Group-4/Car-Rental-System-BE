# Bảo mật

## Access and refresh token cookies

The API uses an `access_token` JWT and a `refresh_token` opaque token in `HttpOnly`, `Secure`, `SameSite=Lax` cookies. The refresh cookie is scoped to `/api/auth`; only a SHA-256 hash is stored in Redis. `POST /api/auth/refresh` rotates it on every use.

Set `Jwt__SigningKey` (at least 32 random characters), `Jwt__Issuer`, and `Jwt__Audience` through environment variables. Never commit the signing key. Token durations can be configured through `Tokens__AccessTokenExpirationMinutes`, `Tokens__RefreshTokenExpirationDays`, and `Tokens__RememberMeRefreshTokenExpirationDays`.

OTP chỉ hash SHA-256 trong Redis và không log; password dùng PBKDF2-SHA512 210,000 iterations/salt riêng. Cần rate-limit theo email + IP tại edge trước production (xem backlog).

Cookie session phải `Secure`, `HttpOnly`, `SameSite=Lax`; Redis key có TTL. Production bắt buộc HTTPS, Brevo API key/DB/Redis URL là environment secret. Không đưa values thật vào appsettings, docs hay GitHub Actions.

Google OAuth callback là `/signin-google`; chỉ dùng client secret ở server. Cloudinary API secret chỉ được dùng server-side để upload signed asset; browser/mobile không được nhận secret. Avatar giới hạn ảnh 5 MB; ảnh xe sẽ dùng cùng `IImageStorage` với folder `vehicle-rental/vehicles` khi FR-009 triển khai.

Rental-policy API yêu cầu role `Admin`, `Manager` hoặc `Staff`. Role `Manager` được seed bằng migration `0005_add_manager_role`; việc gán role cho người dùng thực hiện qua quản trị dữ liệu/luồng quản lý nhân sự được phê duyệt, không qua client.
