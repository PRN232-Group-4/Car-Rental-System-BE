# 2026-09-30 — SQL migration runner và baseline an toàn
- **FR/NFR:** NFR-002, NFR-005
- **Loại:** feat, db, docs

## Đã làm
- Runner khởi động tạo `schema_migrations`, checksum SHA-256 và transaction cho schema migration đầu tiên.
- Dùng `docs/db/Init.sql` làm `0001_initial_schema`; copy script vào publish output/Docker image.
- Database có schema nhưng không history phải explicit baseline một lần, không tự đoán/ghi đè.
- Mọi lỗi migration được ghi `Critical` kèm exception chi tiết trước khi host dừng.
- Nhận `DATABASE_URL` URI chuẩn của Railway/Neon, ngoài `ConnectionStrings__RentalDatabase`.

## Chưa làm
- Chưa có migration schema thứ hai; migration planner tự quét tất cả file SQL và pipeline migration riêng sẽ bổ sung khi schema thay đổi đầu tiên xuất hiện.

## Test
- `dotnet restore --configfile NuGet.Config` và `dotnet build --no-restore` xanh (0 warning, 0 error).
