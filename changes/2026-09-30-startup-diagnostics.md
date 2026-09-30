# 2026-09-30 — Startup database và Redis diagnostics
- **FR/NFR:** NFR-005
- **Loại:** feat, docs
## Đã làm
- Console log một dòng có timestamp; startup ping Redis và xác nhận PostgreSQL trước migration.
- Lỗi connection DB/Redis ghi critical log với configuration cần kiểm tra rồi fail startup.
- Redis multiplexer không abort lúc DI; startup ping sẽ log lỗi rõ ràng sau retry ngắn.
- Hỗ trợ Redis `redis://`/`rediss://` URI và Neon `postgresql://` URI khi chạy cloud.
## Chưa làm
- Chưa có telemetry/export log tập trung.
