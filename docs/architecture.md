# Kiến trúc

API là modular monolith theo Clean Architecture: Web API gọi Application; Application chỉ biết Domain và các port; Infrastructure hiện thực EF Core/PostgreSQL, Redis, Brevo. Dependency đi vào trong, composition ở `Program.cs`.

Từ 2026-10-25, truy vấn đọc thông thường dùng LINQ qua `Persistence/VehicleRentalDbContext` với no-tracking. Row ánh xạ bảng/view hiện có được gom trong `PersistenceRows.cs`; không thêm FK hoặc EF migration. SQL còn dùng cho ghi dữ liệu và kiểm tra/khóa trong transaction. Xem [ADR-012](decisions/ADR-012-entity-framework-read-queries.md) và [rà soát gộp file](file-consolidation-review.md).

## Services và Repositories

```text
src/
├── VehicleRental.Domain/
│   ├── Entities/          # User entity
│   ├── ValueObjects/      # Chờ value object thực tế; README giữ thư mục
│   ├── Services/          # StaffSpecialty: quy tắc specialty/driver
│   └── Interfaces/        # IUserRepository chỉ sử dụng Domain types
├── VehicleRental.Application/
│   ├── Services/          # Use case implementations, security helpers, exceptions
│   ├── Interfaces/        # Services/, Repositories/, External/ ports
│   ├── DTOs/              # Account, staff, rental, payment, request/response và options
│   └── Validators/        # PasswordRules và StaffValidator
├── VehicleRental.Infrastructure/
│   ├── Persistence/       # Migration và hosted services
│   ├── Repositories/      # Npgsql query, mapping, transaction và lock
│   └── Services/          # Authentication/, Registration/, Payments/, Media/ adapters
└── VehicleRental.WebApi/
    ├── Controllers/       # HTTP, DTO mapping và authorization context
    ├── Middlewares/       # Authentication/ và Responses/ (middleware/filter hỗ trợ HTTP)
    ├── Properties/        # launchSettings.json cho local development
    └── Program.cs         # Composition root và DI
```

Luồng gọi: `Controller → Application Service → Repository interface → EF Core query / Npgsql transaction → PostgreSQL`.
Chiều tham chiếu compile-time vẫn đi vào trong: Domain độc lập; Application chỉ tham chiếu Domain;
Infrastructure tham chiếu Application; WebApi tham chiếu Application/Infrastructure để composition.

- `AccountService`: đọc/cập nhật profile, kiểm tra mật khẩu hiện tại, lưu hash mới rồi thu hồi session.
- `SessionService`: đăng nhập, sign-in sau OAuth, refresh và logout. Controller chỉ đọc/ghi cookie.
- `StaffService`: kiểm tra specialty/license, hash mật khẩu và thu hồi session khi đổi thông tin/quyền,
  deactivate hoặc gán department/facility.
- `PaymentService`: checkout, mua gói, xác nhận hủy với provider, xác minh webhook và điều phối
  transaction invoice/subscription. Gateway options được map tại composition root.
- `MediaService`: upload và cập nhật avatar; `UserService`: các thao tác user được gọi từ OAuth.
- `RegistrationService`/`PasswordResetService`: điều phối OTP qua `IWorkflowStateStore`, email,
  user/account repository và `IRefreshTokenStore`; Application không biết Redis/Npgsql/HTTP framework.
- `OperationsService`: entry point cho catalog, rental, dispatch, subscription, maintenance, rating
  và dashboard; các query SQL nằm tại `NpgsqlOperationsRepository`.

Controller không inject Repository hoặc implementation Redis. Interface repository nằm trong namespace
`Car_Rental_System_BE.Application.Interfaces.Repositories`; riêng `IUserRepository` dùng
`Car_Rental_System_BE.Domain.Interfaces` vì port chỉ sử dụng Domain entity và kiểu cơ bản. Implementation SQL dùng
`Car_Rental_System_BE.Infrastructure.Repositories`. Không dùng generic repository.

Interface service nằm trong `Application.Interfaces.Services`, provider/cache/token ports trong
`Application.Interfaces.External`. Request/response DTO đã tách khỏi Controller sang `Application.DTOs`;
DataAnnotations giữ nguyên tên property, validation và JSON schema. `StaffValidator` giữ quy tắc license;
`Domain.Services.StaffSpecialty` giữ quy tắc specialty/driver. Không tạo thêm nghiệp vụ/value object giả
để lấp thư mục. Các thành phần JWT, cookie, OAuth và response filter thuộc `WebApi.Middlewares`.

Access token là JWT trong cookie HttpOnly; refresh token được lưu dạng hash tại Redis qua
`IRefreshTokenStore`. Refresh đối chiếu authorization version và thu hồi token mới nếu quyền đã đổi.
Các adapter Redis/Brevo/Cloudinary/PayOS và JWT issuer được đăng ký ở `Program.cs`.

Schema PostgreSQL không có FK theo chủ đích. Repository transaction hóa thao tác nhiều bảng; use case phải kiểm tra logical ID trước ghi. Khi có aggregate Rental, dùng transaction và lock/constraint thích hợp để chống double booking.

## Phạm vi còn lại

Việc thêm Services/Repositories chưa hoàn tất mọi yêu cầu của Clean Architecture:
`IOperationsRepository` vẫn gom nhiều nhóm nghiệp vụ; Domain vẫn chỉ có User record.
Các quy tắc rental lifecycle, inspection, giá, refund và logical reference đang đọc dữ liệu dưới
database lock vẫn ở repository transaction để giữ hành vi và chống race. Chuyển chúng sang Domain/use case
cần thiết kế aggregate và transaction boundary tiếp theo, không chỉ dời validation ra ngoài transaction.

`ArchitectureTests` kiểm tra chiều dependency và constructor Controller; `ApplicationServiceTests`
kiểm tra use case với persistence/provider ports trong bộ nhớ; regression hiện có vẫn kiểm tra query
và transaction thật trên PostgreSQL riêng.
