# NFR-001 — Clean Architecture

## Reference layout — 2026-10-07

Theo hình cấu trúc do người dùng cung cấp:
- Domain: `Entities`, `ValueObjects`, `Services`, `Interfaces`.
- Application: `Services`, `Interfaces`, `DTOs`, `Validators`.
- Infrastructure: `Persistence`, `Repositories`, `Services`.
- WebApi: `Controllers`, `Middlewares` (các thành phần HTTP hỗ trợ đặt trong thư mục con).

Acceptance: service/repository/provider ports tách khỏi implementation và DTO; request/response
DTO tách khỏi Controller; namespace và project references khớp layout; solution, Docker/publish,
test và liên kết tài liệu trỏ tới `VehicleRental.WebApi` theo tên người dùng xác nhận. Giữ nguyên route, JSON schema và nghiệp vụ.
`ValueObjects` được lưu bằng README cho tới khi có value object thực tế; không tạo model giả.
- **Status:** In progress
Four projects, dependency direction and architecture boundary tests implemented.
Remaining: Domain aggregates and splitting the broad Operations repository; transaction-bound business rules still need migration.

## Services and repositories — 2026-10-07

Controller gọi Application service thay vì repository. `Application/Services` điều phối use case;
`Application/Interfaces/Repositories` chứa persistence ports sử dụng DTO; `Domain/Interfaces` cho `IUserRepository`;
`Infrastructure/Repositories` chứa SQL,
mapping, transaction và lock. Không dùng generic repository.

Acceptance criteria:
- Controller không inject repository hoặc Redis implementation; Application không tham chiếu Infrastructure/WebApi.
- Account/password, staff/session invalidation, media và payment checkout được điều phối tại Application.
- Repository interface tách khỏi DTO/contract và implementation query có thư mục riêng.
- Giữ nguyên route, response envelope, quyền truy cập và tính nguyên tử của thao tác SQL.
- Có test boundary kiến trúc, use case và regression HTTP/refresh authorization.

Phạm vi: tổ chức service/repository và chuyển orchestration; việc tách `IOperationsRepository`
theo aggregate và chuyển toàn bộ invariant đang kiểm tra dưới database lock vào Domain còn tiếp tục.
