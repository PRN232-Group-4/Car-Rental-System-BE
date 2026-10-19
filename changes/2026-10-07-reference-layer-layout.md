# Layout Domain/Application/Infrastructure/API — 2026-10-07

## Thay đổi

Sắp xếp source theo hình tham chiếu người dùng cung cấp, giữ prefix VehicleRental:

- Domain: `Entities`, `ValueObjects`, `Services`, `Interfaces`. User ở Entities; StaffSpecialty
  là Domain policy; IUserRepository chỉ dùng Domain entity/kiểu cơ bản nên chuyển vào Interfaces.
  ValueObjects giữ bằng README tới khi có model thực tế.
- Application: `Services`, `Interfaces`, `DTOs`, `Validators`. Tách interface service/repository/provider,
  request/response DTO và options khỏi implementation/Controller; chuyển license validator vào Validators.
- Infrastructure: `Persistence`, `Repositories`, `Services`. Gom adapter Redis/Brevo/PayOS/Cloudinary
  vào Services; namespace Persistence khớp vị trí migration/hosted services.
- Giữ project/directory/assembly là `VehicleRental.WebApi` theo xác nhận người dùng; Controllers giữ HTTP actions,
  Middlewares chứa authentication/cookie/response/filter hỗ trợ HTTP.
- Cập nhật namespace, solution, test project reference, Docker publish/entrypoint và source links.
- Cập nhật architecture boundary tests cho cả Domain/Application repository ports.

Không có migration, dependency mới, thay đổi route, JSON schema hay nghiệp vụ.

## Kiểm chứng

- Build warnings-as-errors: 0 warning/error.
- 183/183 tests pass với PostgreSQL 18.4 tạm trên loopback; server đã dừng.
- Release publish tạo `VehicleRental.WebApi.dll`, khớp Docker entrypoint mới.
- Docker chưa chạy được vì Docker Desktop engine không hoạt động.
- Tên WebApi được xác nhận lại trong [naming correction](2026-10-07-webapi-project-name.md).

## Liên quan

[NFR-001](../specs/NFR-001-clean-architecture.md), [architecture](../docs/architecture.md),
[ADR-001](../docs/decisions/ADR-001-clean-architecture-repository.md).
