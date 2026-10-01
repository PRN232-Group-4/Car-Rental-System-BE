# NFR-007 — Testing
- **Status:** In progress
- **Priority:** P1

Test project `tests/VehicleRental.Tests` (xUnit). Chiến lược: invariant của hệ thống nằm trong SQL (trigger, CHECK, unique index, transaction) nên integration test chạy trên **PostgreSQL thật** — service container `postgres:16` của CI hoặc `docker compose up -d db` ở local — thay vì mock repository. Unit test chỉ dùng cho logic không chạm DB.

## Acceptance criteria
- [x] Integration test cho FR-019, FR-020, FR-021, FR-022 (mỗi test class một database tạm, áp `docs/db/Init.sql`)
- [x] Unit test xác thực chữ ký webhook PayOS
- [x] Unit test validation DTO ở HTTP boundary
- [ ] Coverage threshold
- [x] Architecture test cho chiều phụ thuộc `Domain <- Application <- Infrastructure <- WebApi`
- [ ] End-to-end HTTP (WebApplicationFactory + Redis + auth)
