# Cookie SameSite cho web cross-site — 2026-10-29

Trình duyệt chặn `Set-Cookie` của `POST /api/auth/login` khi app web chạy ở origin khác
API: `This attempt to set a cookie via a "Set-Cookie" header was blocked because it had
the "SameSite=Lax" attribute but came from a cross-site response`. Hệ quả: login trả
`200`/`204` nhưng request kế tiếp (`GET /api/account/profile`) không có cookie → `401`.

Thay đổi:

- `AuthCookies.Write`/`Clear` nhận thêm `HttpRequest` và chọn `SameSite=None` khi request
  có header `Origin` (browser gọi API ở domain khác — Flutter web `http://localhost:5000`
  → Railway, FE Cloudflare Workers → API).
- Request không có `Origin` (native Android/iOS, server-to-server) giữ `SameSite=Lax`.
- `HttpOnly` và `Secure` không đổi. `Clear` dùng cùng `SameSite` với `Write` để logout xóa
  được cookie (nếu lệch, `Set-Cookie` xóa sẽ không khớp và cookie cũ vẫn còn).

Nguồn: [NFR-004](../specs/NFR-004-security.md), [API](../docs/API.md).

Kiểm chứng: **chưa build, chưa chạy test** (phiên này không chạy `dotnet`). Trước khi
deploy cần `dotnet build` + toàn bộ test BE; sau khi deploy kiểm tra trên Edge tại
`http://localhost:5000`: response `POST /api/auth/login` phải có
`set-cookie: access_token=...; samesite=none; secure`, và request
`GET /api/account/profile` phải có header `cookie`.

Đánh đổi: `SameSite=None` bỏ lớp chống CSRF dựa trên SameSite cho client trình duyệt;
BE hiện chưa có CSRF token (xem mục "Remaining" trong NFR-004), nên bù lại bằng CORS
chặt (whitelist origin, không wildcard) và API chỉ nhận JSON.
