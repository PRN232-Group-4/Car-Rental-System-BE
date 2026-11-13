# NFR-001 — Clean Architecture

## Kiểm chứng foreign key — 2026-11-13

- Mọi cột tham chiếu quan hệ phải có foreign key thật trong PostgreSQL, khớp bảng/cột đích của EF model; không chỉ khai báo quan hệ ở ORM.
- Các bảng danh mục/gốc độc lập không bắt buộc có FK đi ra. `customer_loyalty_transactions.ref_id` là tham chiếu đa hình theo `ref_type`, không gắn cố định với một bảng.
- Acceptance: kiểm tra catalog sau khi áp toàn bộ migration; không thiếu FK, không còn FK chưa validate trên DB sạch; chạy lại migration không tạo trùng; migration trên dữ liệu cũ mồ côi giữ dữ liệu nhưng chặn tham chiếu sai mới.

## Hoàn tất ORM cho truy vấn dữ liệu — 2026-11-13

- Mọi truy vấn đọc/ghi dữ liệu nghiệp vụ dùng EF Core, kể cả trong transaction (ADR-014).
- Migration runner đọc/ghi `schema_migrations` và kiểm tra catalog PostgreSQL bằng một EF context riêng, hoạt động trước khi schema nghiệp vụ tồn tại.
- Chỉ giữ SQL cho primitive khóa/đồng hồ DB trong `DbLocks` và DDL/bootstrap/file migration. Không thay đồng hồ DB bằng đồng hồ ứng dụng hoặc bỏ khóa để loại SQL.
- Acceptance: rà toàn bộ `src` không còn API raw SQL/ADO ngoài hai vị trí trên; migration vẫn giữ checksum, baseline, rollback và tính nguyên tử giữa script với history; có regression trên PostgreSQL tạm.

## Gộp phần Customer detail còn lại — 2026-10-25

- Gộp `CustomerService.Detail.cs` vào `CustomerService.cs` và `CustomersController.Detail.cs` vào `CustomersController.cs`; bỏ `partial` khi lớp không còn phần khai báo khác.
- Giữ DTO `CustomerDetail` và test chi tiết riêng vì chúng mô tả contract/tình huống kiểm chứng, không phải file implementation bị chia nhỏ.
- Acceptance: không còn file `*Detail*` trong `src`; service/controller gọi cùng use case và giữ dữ liệu, quyền, xử lý not-found; build và regression Customer detail đạt.

Xem [thay đổi](../changes/2026-10-25-file-consolidation-api-number.md).

## Entity Framework và tổ chức file — 2026-10-25

- Truy vấn đọc dữ liệu thông thường ở Infrastructure dùng Entity Framework Core với provider PostgreSQL, LINQ projection và no-tracking; không đưa EF vào Domain/Application.
- Ánh xạ schema hiện có, không tự tạo/chạy EF migration, không thêm foreign key hoặc thay đổi contract API.
- Giữ SQL đặc thù PostgreSQL cho ghi nguyên tử, trigger, advisory/row lock và các kiểm tra trong transaction cần giữ thứ tự khóa. EF tham gia cùng connection/transaction khi đọc trong transaction.
- Phân trang, tổng sau lọc, thứ tự ổn định, kết quả trang rỗng và phép tính nghiệp vụ phải giữ nguyên.
- Gộp file nhỏ khi cùng feature/contract; giữ namespace, public type và ranh giới kiến trúc. Không gom controller/service/repository vào một file.
- Kiểm chứng bằng build, regression hiện có và test mapping/truy vấn trên PostgreSQL tạm, không chạy trên DB ứng dụng.
- Hoàn tất regression: sửa fixture/test lỗi cũ cho phù hợp invariant GPLX, hợp đồng đã upload và quyền hiện hành; giữ assertions kiểm tra nghiệp vụ. Toàn suite phải xanh, không skip ca lỗi hoặc giảm validation sản phẩm để làm test qua.

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
