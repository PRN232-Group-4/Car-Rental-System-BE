# 2026-10-01 — Serilog, correlation ID, Redis health, architecture test
- **FR/NFR:** NFR-001, NFR-005, NFR-007
- **Loại:** feat + test

## Đã làm

- Serilog (`Serilog.AspNetCore`) thay cho `AddSimpleConsole`: cấu hình qua section `Serilog` trong `appsettings.json`, enrich `FromLogContext`, sink Console. Structured logging đóng NFR-005.
- Middleware `CorrelationIdMiddleware`: đọc/ sinh `X-Correlation-Id`, ghi vào `TraceIdentifier` + response header, push property `CorrelationId` vào LogContext.
- `/health` kiểm tra thêm Redis (`IConnectionMultiplexer.PingAsync`) bên cạnh PostgreSQL; 503 kèm message khi DB hoặc Redis chết.
- Test `ArchitectureTests`: đọc `ProjectReference` từ các `.csproj`, assert chiều phụ thuộc `Domain <- Application <- Infrastructure <- WebApi` (không cần thêm package).

## Chưa làm

- Coverage threshold (NFR-007).
- End-to-end HTTP (WebApplicationFactory + Redis + auth).

## Test

- `dotnet build Car-Rental-System-BE.sln`: xanh, 0 warning (TreatWarningsAsErrors).
- `dotnet test --filter "ArchitectureTests|UnitTests"`: 7/7 pass (không cần Postgres).

## Quyết định/giả định

- Giữ endpoint `/health` thủ công thay vì `AddHealthChecks` + package `AspNetCore.HealthChecks.*` để không thêm dependency; health check dùng chính `NpgsqlDataSource`/`IConnectionMultiplexer` đã đăng ký.
- Architecture test parse `.csproj` thay vì dùng NetArchTest.Rules: đúng trọng tâm "project reference", không thêm package.
