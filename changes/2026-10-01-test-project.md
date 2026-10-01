# 2026-10-01 — Test project cho invoice/payment/subscription
- **FR/NFR:** NFR-007, FR-019, FR-020, FR-021, FR-022
- **Loại:** test

## Đã làm

- Thêm `tests/VehicleRental.Tests` (xUnit, net8.0), đưa vào solution folder `tests`.
- Fixture `PostgresFixture` tạo một database tạm cho mỗi test class và áp `docs/db/Init.sql`, nên trigger, CHECK, unique index và transaction được chạy thật. `DROP DATABASE ... WITH (FORCE)` khi kết thúc.
- 16 integration test:
  - FR-019: invoice gồm VAT + cọc, idempotent & duy nhất theo rental, chặn subscription ở cả Application và DB trigger, customer chỉ thấy hóa đơn của mình.
  - FR-020/021: webhook idempotent theo `provider_reference`, chặn over-refund, duyệt/từ chối refund kèm audit, huỷ refund giải phóng hạn mức, post-inspection đạt tự tạo hoàn cọc.
  - FR-022: tạo/cập nhật plan, plan inactive bị ẩn và không mua được, mua plan đúng date range/hours, xóa cứng khi chưa dùng và soft-delete khi đã dùng.
- 3 unit test: xác thực chữ ký PayOS (đúng/sai/không phải hex), validation `PlanRequest` và `RefundRequest`.

## Chưa làm

- Architecture test cho chiều phụ thuộc project.
- Coverage threshold.
- End-to-end HTTP (WebApplicationFactory + Redis + auth).

## Test

- `dotnet build Car-Rental-System-BE.sln --no-restore -warnaserror`: xanh.
- `dotnet test Car-Rental-System-BE.sln --no-build`: 19/19 pass trên `postgres:16-alpine` local.

## Quyết định/giả định

- Không dùng Testcontainers: CI đã có service container `postgres:16` + `redis:7`, local dùng `docker compose up -d db`. Fixture đọc `TEST_DATABASE_ADMIN` (mặc định `Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres`).
- Không mock repository vì phần lớn invariant nằm trong SQL.
- Test dùng repository trực tiếp, chưa chạm tầng HTTP/auth để tránh phụ thuộc Redis + luồng OTP.
