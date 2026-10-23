# Caddy đứng trước API trong deploy docker compose — 2026-10-23

## Bối cảnh

Yêu cầu: thêm mount `./Caddyfile:/etc/caddy/Caddyfile` vào deploy docker của BE. Repo trước đó **không có** service Caddy, không có `Caddyfile`, và compose chỉ gồm `db`, `api`, `redis` (Railway dùng `Dockerfile` + `railway.toml`, không dùng compose). Vì vậy chỉ thêm dòng `volumes` là chưa đủ: bind mount trỏ tới file không tồn tại sẽ khiến Docker tạo **thư mục** tên `Caddyfile` và không có container nào dùng nó.

## Đã làm

- `docker-compose.yml`: thêm service `caddy` (`caddy:2-alpine`) với
  - `- ./Caddyfile:/etc/caddy/Caddyfile` (đúng yêu cầu),
  - `caddy_data:/data` và `caddy_config:/config` để chứng chỉ ACME không bị xin lại mỗi lần restart,
  - publish `${CADDY_HTTP_PORT:-80}:80` và `${CADDY_HTTPS_PORT:-443}:443`,
  - `CADDY_SITE_ADDRESS: ${CADDY_SITE_ADDRESS:-:80}` và `depends_on: api`.
- `Caddyfile` mới ở gốc repo: site address lấy từ `{$CADDY_SITE_ADDRESS::80}`, `encode zstd gzip`, `reverse_proxy api:8080`. Mặc định `:80` = HTTP mọi hostname (không tự xin chứng chỉ); đặt tên miền thật thì Caddy tự bật HTTPS.
- `docs/deployment.md`: thêm mục “Reverse proxy Caddy khi chạy bằng docker compose” (cách đổi tên miền, đổi cổng, kiểm tra nhanh, và ghi rõ bản Railway không dùng Caddy này).

## Giả định

- Chưa biết tên miền triển khai nên mặc định để `:80` và không hard-code domain; đổi bằng biến môi trường, không cần sửa file.
- Compose là môi trường tự vận hành; Railway vẫn là đường deploy production hiện tại và không bị ảnh hưởng.
- `api` giữ nguyên `ports: 3000:8080` cho FE dev; Caddy proxy tới cổng nội bộ `8080`.

## Kiểm chứng (chạy thật, Docker Desktop trên Windows)

- `docker compose config` phân tích file thành công; 3 volume `postgres_data`, `caddy_data`, `caddy_config` được nhận.
- `CADDY_HTTP_PORT=8081 CADDY_HTTPS_PORT=8443 docker compose up -d --no-deps caddy`: container `Up`, log có `using config from file "/etc/caddy/Caddyfile"`, `server running`, `serving initial configuration`.
- `docker compose exec -T caddy ls -l /etc/caddy/` → `-rwxrwxrwx ... Caddyfile` (file 1257 bytes, không phải thư mục) ⇒ bind mount đúng.
- `GET http://localhost:8081/health` → `502 Bad Gateway` (api chưa chạy) ⇒ Caddy đã nhận request và chuyển tiếp tới `api:8080`.
- `CADDY_SITE_ADDRESS=api.example.com caddy adapt …` → server listen `:443` với host matcher `api.example.com`, `encode zstd/gzip`, `reverse_proxy dial api:8080` ⇒ nhánh HTTPS hoạt động.
- Dọn dẹp: container `caddy` và hai volume test đã được xoá; container `db` được dựng lại để test tích hợp tiếp tục chạy được.

## Chưa làm

- Chưa mount `Caddyfile` cho môi trường Railway (Railway không dùng compose).
- Chưa cấu hình TLS nội bộ/`internal` cho môi trường không có Internet, chưa đặt header bảo mật bổ sung (HSTS, CSP…) — thêm khi có yêu cầu cụ thể.
