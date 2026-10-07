# Services và Repositories — 2026-10-07

## Thay đổi

Controller trước đây inject repository và một số Redis implementation, đồng thời tự điều phối
password/session, staff permission invalidation và checkout. Nay controller gọi Application Service.

- Thêm `Application/Services` cho Account, Session, Staff, User, Media, Operations, Payment,
  Registration và PasswordReset, cùng service/workflow interfaces.
- Tách 5 repository interfaces khỏi DTO/contracts sang `Application/Repositories`.
- Chuyển Npgsql implementation và các partial Operations files sang `Infrastructure/Repositories`.
- Thêm `IRefreshTokenStore`, `IAccessTokenIssuer`, `IWorkflowStateStore` để Application chỉ biết ports.
- Chuyển OTP/reset orchestration từ Redis workflow sang Application; Redis adapter chỉ lưu/đọc state.
- Đưa credential invalidation, password hashing/verification, avatar persistence và payment
  checkout/cancel/webhook orchestration vào Services. Giữ controller validation/error mapping và policy.
- Chuyển Google HTTP options sang WebApi; cập nhật DI và namespace imports trong integration tests.
- Giữ SQL, transaction/lock và các invariant đang chạy dưới lock. Không có migration, đổi route
  hoặc đổi API response contract. Chưa tách Operations theo aggregate/chuyển toàn bộ rules sang Domain.

## Kiểm chứng

Build warnings-as-errors thành công, 0 warning/error. Test kiến trúc/use case/HTTP/authorization:
124/124 pass. Full suite: **183/183 pass** với PostgreSQL 18.4 tạm trên loopback, server đã dừng.
Không chạy test với database ứng dụng hoặc gọi provider thật. Docker build chưa kiểm chứng vì
Docker Desktop engine không chạy. `git diff --check` sạch.

## Liên quan

- [NFR-001](../specs/NFR-001-clean-architecture.md)
- [Architecture](../docs/architecture.md)
- [ADR-001](../docs/decisions/ADR-001-clean-architecture-repository.md)
