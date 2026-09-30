# 2026-09-30 — Bootstrap Clean Architecture, OTP, Redis, Brevo, Docker/CI
- **FR/NFR:** FR-001, FR-002, FR-003, FR-028, NFR-001..005, NFR-008
- **Loại:** feat, docs, ci

## Đã làm
- Scaffold Domain/Application/Infrastructure, repository `IUserRepository` với Npgsql implementation.
- API registration/verify OTP; pending data Redis, Brevo sender, PBKDF2 hash.
- Redis `ITicketStore`, cookie security defaults; Docker compose Redis, Railway, workflows, docs/specs.

## Chưa làm
- Login/logout/reset, Redis distributed rate-limit, CSRF/security headers, test suite, migration runner, production Railway verification.

## Test
- `dotnet restore --configfile NuGet.Config` và `dotnet build --no-restore` đã xanh (0 warning, 0 error).
- Chưa có test project; `dotnet test --no-build` không tìm thấy test để chạy. Đây là follow-up NFR-007.

## Quyết định/giả định
- Xem ADR-001, `docs/open-questions.md`, `docs/optimization-backlog.md`.
