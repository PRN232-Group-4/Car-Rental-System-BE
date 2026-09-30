# 2026-09-30 — Startup URLs và Swagger contract
- **FR/NFR:** NFR-008
- **Loại:** docs, feat
## Đã làm
- Khi web server start, console in tất cả URL listen thực tế và URL `/swagger` ngoài production.
- AGENT Definition of Done bắt buộc endpoint mới có Swagger metadata và không được ẩn tùy tiện.
## Chưa làm
- Chưa có API versioning/examples/auth schema OpenAPI.

## Ghi chú môi trường
- `global.json` được pin .NET SDK 8.0.408; máy phát triển cần cài .NET 8 SDK để chạy lệnh `dotnet` trong repo.
