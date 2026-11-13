# Kiến trúc

Backend là modular monolith theo Clean Architecture, giữ chiều dependency `Domain <- Application <- Infrastructure <- WebApi`. Nghiệp vụ được tổ chức theo `Features/<Module>` trong mỗi layer, tích hợp từ `refactor/features-layout` và giữ ORM/FK theo ADR-014. Xem [ADR-015](decisions/ADR-015-features-layout-with-ef.md).

```text
src/
├── VehicleRental.Domain/
│   ├── Features/                    # Assignments, Availability, Billing, Loyalty, Maps, Rentals
│   └── Entities/ ValueObjects/ Services/ Interfaces/
├── VehicleRental.Application/
│   ├── Features/<Module>/           # Use case, service/repository port, DTO của module
│   └── Services/ Interfaces/ DTOs/ Validators/ Common/ Abstractions/
├── VehicleRental.Infrastructure/
│   ├── Features/<Module>/           # Repository và adapter theo nghiệp vụ
│   ├── Persistence/Rows/            # Row mapping theo module
│   ├── Persistence/                 # DbContext, DbLocks, transaction, timestamp
│   ├── Repositories/ Services/      # Các thành phần dùng chung/còn lại
│   ├── HostedServices/              # Migration, expiry, telemetry retention
│   └── DependencyInjection.cs       # Đăng ký Infrastructure
└── VehicleRental.WebApi/
    ├── Features/<Module>/           # Controller/endpoint của module
    ├── Controllers/ Middlewares/    # HTTP/auth/response dùng chung
    ├── Extensions/                  # Composition và cấu hình host
    └── Program.cs

tests/
├── VehicleRental.Domain.Tests/
├── VehicleRental.Application.Tests/
├── VehicleRental.Integration.Tests/
└── VehicleRental.Architecture.Tests/
```

Các module Application gồm Assignments, Availability, Billing, Customers, Dashboard, Fleet, Handovers, Inspections, Loyalty, Maintenances, Maps, Organization, Ratings, Recovery, RentalPolicies, Rentals, Reports và Tracking. Namespace theo `Car_Rental_System_BE.<Layer>.Features.<Module>`; các kiểu dùng chung giữ namespace hiện có.

Controller gọi Application service, service gọi repository/provider port. Domain và Application không tham chiếu EF Core, Npgsql, HTTP hoặc Redis. Infrastructure hiện thực port bằng EF Core/PostgreSQL, Redis và các provider ngoài; WebApi đăng ký dependency ở composition root. Không dùng generic repository.

Đọc và ghi dữ liệu đều qua `VehicleRentalDbContext`/EF Core. `Persistence/Rows` giữ mapping schema SQL hiện có, default, generated column và FK. SQL runtime chỉ còn primitive khóa PostgreSQL/đồng hồ DB trong `DbLocks` và DDL/file migration; không dùng EF Migrations. Các migration immutable được định danh bằng tên file đầy đủ và kiểm checksum.

Quyết toán rental dùng một transaction với thứ tự advisory lock, khóa rental, rồi khóa subscription. Writer hoàn tiền, hóa đơn, ledger giờ gói và loyalty dùng cùng connection/transaction qua `AmbientTransactionScope`, nên lỗi ở bất kỳ bước nào rollback toàn bộ. `subscription_hour_ledger` bảo đảm một rental không bị trừ giờ gói hai lần. Hóa đơn supplementary thu được bằng cash hoặc PayOS theo cùng quy tắc phí sau trả xe.

Schema có FK vật lý; Application vẫn xác minh logical reference để trả lỗi nghiệp vụ. PostgreSQL giữ trigger, exclusion constraint và lock chống trùng lịch; không di chuyển invariant ra khỏi transaction chỉ để đổi cách tổ chức thư mục.

Access token là JWT trong cookie HttpOnly, refresh token chỉ lưu hash trong Redis. Mỗi request/refresh đối chiếu authorization version để thu hồi phiên khi thay đổi tài khoản/quyền/mật khẩu. OTP/password và dữ liệu provider được quản lý sau các Application port.

Bộ test Domain/Application kiểm tra quy tắc và orchestration; Integration tạo PostgreSQL dùng một lần, áp toàn bộ migration rồi kiểm API, tiền, rollback và concurrency. Architecture kiểm dependency ở mức signature/IL, privacy giữa các feature và giới hạn raw query. Catalog PostgreSQL phải khớp toàn bộ FK trong EF model.
