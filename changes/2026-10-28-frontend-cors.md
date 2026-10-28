# CORS cho frontend — 2026-10-28

Thêm default CORS policy trong composition root WebApi cho đúng ba origin:

- `https://car-rental-system-fe.phucthan308.workers.dev`
- `http://localhost:3000`
- `http://localhost:5000`

Cho phép mọi request header/method và credentials; xử lý preflight trước rate limiting/authentication/authorization. Origin không chứa dấu `/` cuối. Không thay đổi cookie SameSite/Secure.

Nguồn: [NFR-004](../specs/NFR-004-security.md), [API](../docs/API.md). Chưa deploy; cần triển khai lại BE để áp dụng.

Kiểm chứng: build solution `--no-restore -warnaserror -m:1 -p:UseSharedCompilation=false -nodeReuse:false` đạt 0 warning/error; 34/34 `ApiResponseTests` hiện có đạt. Chưa kiểm chứng trình duyệt với FE deploy.
