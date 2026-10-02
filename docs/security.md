# Bảo mật

OTP chỉ hash SHA-256 trong Redis và không log; password dùng PBKDF2-SHA512 210,000 iterations/salt riêng. Cần rate-limit theo email + IP tại edge trước production (xem backlog).

Cookie session phải `Secure`, `HttpOnly`, `SameSite=Lax`; Redis key có TTL. Production bắt buộc HTTPS, Brevo API key/DB/Redis URL là environment secret. Không đưa values thật vào appsettings, docs hay GitHub Actions.

Google OAuth callback là `/signin-google`; chỉ dùng client secret ở server. Cloudinary API secret chỉ được dùng server-side để upload signed asset; browser/mobile không được nhận secret. Avatar giới hạn ảnh 5 MB; ảnh xe sẽ dùng cùng `IImageStorage` với folder `vehicle-rental/vehicles` khi FR-009 triển khai.

Rental-policy API yêu cầu role `Admin`, `Manager` hoặc `Staff`. Role `Manager` được seed bằng migration `0005_add_manager_role`; việc gán role cho người dùng thực hiện qua quản trị dữ liệu/luồng quản lý nhân sự được phê duyệt, không qua client.
