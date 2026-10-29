# NFR-004 — Security
- **Status:** In progress
Secure cookie, PBKDF2, OTP TTL/attempt/cooldown implemented. Authentication request limits default to 5 requests/60 seconds/IP/instance (password-change endpoints share a budget; other endpoints have separate budgets). Login/current-password checks reserve at most 5 attempts/10 minutes/normalized email in Redis; the fifth failure returns 429, success before lockout resets the counter, expiry permits retry. OTP attempt increments/consumption are atomic and reject the fifth failure; attempt settings cannot exceed 5. Request limits include invalid input. Remaining: distributed IP/email request rate limiting across all flows, CSRF, headers, secret scanning.

## CORS — 2026-10-28

- [x] Allow only `https://car-rental-system-fe.phucthan308.workers.dev`, `http://localhost:3000`, and `http://localhost:5000` in all environments.
- [x] Allow any request method/header and credentials for these origins; no wildcard origin.
- [x] Process CORS preflight before rate limiting, authentication, and authorization. Existing cookie SameSite/Secure rules still apply.

Change: [Frontend CORS](../changes/2026-10-28-frontend-cors.md).

## Cookie SameSite — 2026-10-29

- [x] Request có header `Origin` (browser cross-site: Flutter web `http://localhost:5000` → API Railway, FE Cloudflare Workers → API) nhận cookie `HttpOnly`, `Secure`, `SameSite=None`.
- [x] Request không có `Origin` (native Android/iOS, server-to-server) giữ `SameSite=Lax`.
- [x] `Clear` dùng cùng `SameSite` với `Write` để logout xóa đúng cookie.
- [ ] CSRF token — vẫn còn thiếu; `SameSite=None` bỏ lớp bảo vệ CSRF dựa trên SameSite cho client trình duyệt, hiện bù bằng CORS whitelist + API chỉ nhận JSON.

Change: [Cookie SameSite](../changes/2026-10-29-cookie-samesite-cross-site.md).
