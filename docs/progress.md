# Tiến độ

## BE-27 (FR-029) Loyalty Phase 1 (Điểm thưởng) — 2026-10-14

Hiện thực toàn diện module Loyalty Phase 1 theo đặc tả `specs/FR-029-loyalty.md`:
1. **Migration 0018 (`0018_loyalty.sql`)**: Tạo bảng `customer_loyalty_transactions`, triggers `trg_clt_before_insert` (tính balance_after dưới khóa dòng FOR UPDATE) và `trg_clt_after_insert` (đồng bộ `customers.loyalty_point`), immutability triggers chặn UPDATE/DELETE/TRUNCATE, partial unique indexes chặn cộng đôi khi retry/concurrency, view đối soát `v_loyalty_reconciliation`, nạp số dư đầu kỳ idempotent.
2. **Domain Layer**: `LoyaltyDomainRules` tính toán phân hạng (`Member`, `Silver`, `Gold`, `Platinum`), hệ số nhân (1.0x, 1.1x, 1.25x, 1.5x), tích điểm theo net rent, báo giá tiêu điểm và thẩm định quy tắc; 23 unit tests xanh 100%.
3. **Application Layer**: `LoyaltyOptions` (POCO + ValidateOnStart), `LoyaltyContracts` (DTOs/Interfaces), `LoyaltyService` thực hiện nghiệp vụ tóm tắt, lịch sử, điều chỉnh điểm thủ công, báo giá quy đổi; 7 unit tests xanh 100%.
4. **Infrastructure Layer**: `NpgsqlLoyaltyRepository` ánh xạ lỗi DB (23514 -> 409 INSUFFICIENT_POINTS, 23505 -> Idempotent DTO, AwardForRentalAsync ON CONFLICT DO NOTHING); cập nhật `LoyaltyAwardAdapter` tính điểm chuẩn xác theo net rent và gỡ bỏ hoàn toàn lệnh update trực tiếp `customers.loyalty_point`.
5. **WebApi Layer**: `LoyaltyController` (`GET /api/loyalty/me`, `GET /api/loyalty/me/transactions`, `GET /api/customers/{id}/loyalty`, `POST /api/customers/{id}/loyalty/adjustments`, `GET /api/loyalty/redemption-quote`, `GET /api/admin/loyalty/reconciliation`); chuẩn hóa messages trong `ApiSuccessResponseFilter`; đăng ký DI trong `Program.cs`. 7 controller tests xanh 100%.
6. **Kiểm thử tích hợp & Concurrency**: 5 kịch bản tích hợp hoàn tất (tích điểm khi complete, 2 request complete song song chỉ ghi 1 dòng, 200 thao tác ngẫu nhiên không gây drift, đơn subscription không tích điểm, kiểm tra số dư âm và idempotency). Toàn bộ 49/49 loyalty tests xanh 100%.
7. Xem [ADR-005](decisions/ADR-005-loyalty-ledger.md) và [change](../changes/2026-10-14-be-27-loyalty-phase1.md).

## Rule pagination — 2026-10-10

AGENTS.md và Definition of Done bắt buộc mọi API list dùng page=1/pageSize=20, pageSize tối đa 100, data có items/page/pageSize/totalCount, validation tại HTTP và Application. NFR-008/API reference/ADR-003 đã đồng bộ. Đây là thay đổi rule/tài liệu; các API cũ còn trả array chưa được chuyển đổi trong lượt này. Kiểm tra diff/link tài liệu, không chạy lại build/test do không sửa runtime. Xem [change](../changes/2026-10-10-list-pagination-rule.md).

## Customer directory và upload Cloudinary — 2026-10-10

Thêm GET danh sách/chi tiết customer cho Admin/Manager/CustomerService, có phân trang/tìm kiếm/lọc active và profile/license/loyalty an toàn. PUT customer license nhận multipart và lưu secureUrl sau upload, đặt lại pending; JSON URL chuyển sang `/api/customer/license/url`. Bổ sung upload ảnh bằng lái nhân viên, inspection và report theo quyền tạo dữ liệu tương ứng. Avatar/ảnh xe đã có upload. Lỗi Cloudinary trả 500 an toàn, không ghi license khi provider thất bại.

Build warnings-as-errors thành công; **249/249 test pass**, gồm HTTP multipart/Swagger/envelope/auth, Application và PostgreSQL 18.4 riêng. PostgreSQL kiểm thử đã dừng. Cloudinary dùng stub; chưa kiểm chứng upload vào tài khoản thật. Không migration; private delivery/cleanup ảnh cũ và Docker còn chưa kiểm chứng. Xem [change](../changes/2026-10-10-customer-directory-media.md) và [API](API.md).

## Giới hạn mật khẩu — 2026-10-07

Đăng ký/đăng nhập dùng giới hạn chung **6–20 ký tự**, bao gồm hai mốc; tạo nhân viên/đổi/reset mật khẩu mới dùng cùng rule để tránh tạo credential vượt giới hạn login. Lỗi trả 400 với envelope hiện tại và thông báo tiếng Việt. Swagger đọc minLength/maxLength từ record parameters; AGENTS và API reference đã cập nhật. Build 0 warning/error; **110/110** test response/validation/unit/authorization/refresh authorization pass, gồm các mốc 5/6/20/21. Không thay hash/session hoặc migration; không chạy lại DB suite do lỗi xác thực local đã ghi. Account cũ có password >20 cần reset. Xem [change](../changes/2026-10-07-password-length.md).

## WebApi project naming correction — 2026-10-07

Theo xác nhận người dùng, project/folder/assembly giữ tên `VehicleRental.WebApi`.
Namespace HTTP dùng `Car_Rental_System_BE.WebApi`; solution/test references, Docker và source links đã cập nhật.
Build và Release publish thành công, 0 warning/error; 124/124 regression tests pass.
Các thư mục con và nghiệp vụ giữ nguyên. Thư mục `VehicleRental.API` đã xóa hết source/build artifacts
nhưng root rỗng còn bị Windows process khóa. Không còn project/reference ở đó.
Xem [change](../changes/2026-10-07-webapi-project-name.md).

## Reference source layout — 2026-10-07

Sắp xếp theo hình người dùng: Domain (`Entities`, `ValueObjects`, `Services`, `Interfaces`);
Application (`Services`, `Interfaces`, `DTOs`, `Validators`); Infrastructure
(`Persistence`, `Repositories`, `Services`); API (`Controllers`, `Middlewares`).
Request/response DTO tách khỏi Controller, port tách khỏi DTO/implementation, namespace khớp folder.
Project/directory/assembly giữ tên `VehicleRental.WebApi`; solution, Docker, tests và source links cập nhật.

Build warnings-as-errors 0 warning/error; **183/183 test pass** trên PostgreSQL 18.4 độc lập,
server đã dừng. Release publish tạo đúng `VehicleRental.WebApi.dll`. Docker engine chưa chạy nên
chưa kiểm chứng Docker build. Tên project WebApi đã được xác nhận lại ở cập nhật phía trên.
Không migration/thay đổi API contract/nghiệp vụ.
Xem [change](../changes/2026-10-07-reference-layer-layout.md).

## Services/Repositories — 2026-10-07

Thêm Application Services cho account/session/staff/media/payment/operations/user và OTP/reset;
Controller gọi service thay vì repository/Redis implementation. Repository interfaces tách khỏi DTO
sang `Application/Repositories`; các query Npgsql ở `Infrastructure/Repositories`.
Application dùng cache/session/token/email/payment ports, không tham chiếu HTTP/Redis/Npgsql.

Build warnings-as-errors: 0 warning/error. Boundary/use-case/HTTP/auth tests: 124/124 pass;
toàn bộ suite: **183/183 pass** trên PostgreSQL 18.4 test riêng và đã dừng server sau chạy.
Diff whitespace sạch. Docker chưa kiểm chứng do Docker Desktop engine không chạy.
Không có migration, đổi route hay response contract. Operations còn cần tách theo aggregate;
invariant đang đọc dữ liệu dưới database lock vẫn giữ tại repository transaction.
Xem [change](../changes/2026-10-07-services-repositories.md) và [architecture](architecture.md).

## Response contract — 2026-10-07

Quy tắc duy trì contract đã được ghi trong [AGENTS.md](../AGENTS.md), áp dụng cho các lần thêm/sửa API tiếp theo; Definition of Done dùng ProblemDetails trong `error`.

Chuẩn hóa response JSON success/error thành `code`, `status`, `is_success`, `data`, `message`, `error`; code khớp HTTP, status success/error, is_success boolean. MVC always-run filter xử lý automatic validation; middleware bao phủ exception/auth/route; health và Swagger dùng contract tương ứng. Build không warning/error; **97/97** test response HTTP, unit, authorization và refresh authorization pass. Nhóm PostgreSQL bị lỗi xác thực DB local ở lượt lọc ban đầu nên chưa xác nhận lại toàn bộ integration suite. Không migration; không gọi PayOS/Redis/DB triển khai. Xem [change](../changes/2026-10-07-api-response-contract.md) và [ADR-002](decisions/ADR-002-api-response-envelope.md).

## Tài liệu API — 2026-10-07

Thêm [API reference tiếng Việt](API.md) cho 90 controller endpoints, các route dịch vụ và callback OAuth. Có quyền truy cập, cookie/refresh, DTO, mã HTTP sau response filter, quy tắc thuê/thanh toán và ví dụ frontend. Kiểm tra danh sách route với controller, JSON minh họa và link tài liệu nội bộ; chưa gọi hệ thống chạy thật. Chỉ thay đổi tài liệu, không có migration hoặc thay đổi runtime. NFR-008 vẫn In progress. Xem [change](../changes/2026-10-07-api-documentation.md).

## BE-12/BE-13/BE-15 audit — 2026-10-05
Hourly/long-term creation, transactional interval locks and pre-inspection gating already exist. Added signed contract PUT API and exposed unit-price snapshots/contract metadata in rental responses. Build/test with warnings as errors: 131/131 tests pass against isolated PostgreSQL 18. Corrected shared vehicle test seed to include required category/fuel. No migration required; Docker deployment not verified. FR-013 renewal/payment schedules, FR-017 automated overtime and dedicated Domain Rental aggregate remain pending. See [API guide](rental-contract-api.md).

| ID | Tên | Milestone | Priority | Status | Spec | Change gần nhất |
|---|---|---|---|---|---|---|
| Bootstrap | Docs, architecture, Docker, CI | M0 | P0 | In progress | NFR-001..008 | 2026-10-02-local-migration-path |
| FR-001 | Đăng ký OTP | M2 | P0 | In progress | FR-001 | 2026-09-30-account-basics |
| FR-002 | Verify/resend OTP | M2 | P0 | In progress | FR-002 | 2026-09-30-account-basics |
| FR-003 | Access/refresh token cookies | M2 | P0 | In progress | FR-003 | 2026-10-03-access-refresh-token-cookies |
| FR-004..007 | Account/RBAC | M2 | P0-P1 | In progress | FR-004..007 | 2026-10-03-staff-authorization |
| FR-008..009,011 | Catalog/operations | M3 | P0-P1 | In progress | FR-008..011 | 2026-10-03-vehicle-search-envelope-fixes |
| FR-010 | Tìm xe & availability (BE-10) | M3 | P0 | Done | FR-010 | 2026-10-04-vehicle-availability |
| FR-014 | Phân xe/tài xế & Đổi xe (BE-14) | M4 | P0 | Done | FR-014 | 2026-10-04-assignment-vehicle-swap |
| FR-018 | Vòng đời đơn thuê, Quá giờ & Quyết toán (BE-17) | M4 | P0 | Done | FR-018 | 2026-10-06-rental-lifecycle |
| FR-012,013,015..017 | Rentals | M4 | P0-P1 | In progress | FR-012..017 | 2026-10-06-rental-lifecycle |
| FR-019..021 | Payments | M5 | P1 | In progress | FR-019..021 | 2026-10-03-subscription-plan-payments |
| FR-022..025 | Subscription | M6 | P0-P1 | Done | FR-022..025 | 2026-10-03-subscription-plan-payments |
| FR-026..027,029..030 | Maintenance/report/loyalty/dashboard | M7 | P1-P2 | In progress | FR-026..027,029..030 | 2026-10-03-staff-authorization |
| FR-028 | Brevo notifications | M7 | P0 | In progress | FR-028 | 2026-09-30-bootstrap |
| FR-032 | Customer directory list/detail | M2 | P1 | Done | FR-032 | 2026-10-10-customer-directory-media |
| NFR-001..005,008 | Architecture/platform/security | M0 | P0 | In progress | NFR files | 2026-09-30-bootstrap |
| NFR-006..007 | Performance/testing | M0 | P1 | In progress | NFR files | 2026-10-01-test-project |

## Tổng kết
Done: FR-010 (tìm xe và availability), FR-014 (phân xe/tài xế & đổi xe), FR-018 (vòng đời đơn thuê & quyết toán), FR-022..025 (subscription, gồm cả thanh toán mua gói); In progress: các nhóm còn lại; Not started: các nhóm chưa chạm. `dotnet build -warnaserror` đã xanh 0 warning/0 error; test suite pass 100% (318/318 tests) ngày 2026-10-06.

## Vòng đời đơn thuê, Quá giờ & Quyết toán (FR-018 / BE-17) — 2026-10-06

Đã hoàn thành module BE-17 theo kiến trúc Clean Architecture:
- Migration `0013_rental_lifecycle.sql`: Ràng buộc `ck_rentals_status`, các mốc thời gian vòng đời trên `rentals`, bảng `rental_status_history` kèm trigger bất biến `trg_rsh_immutable`, bảng quyết toán `rental_settlements`, cột `kind` trên bảng `invoices` kèm partial unique index `ux_invoices_rental_kind`.
- Domain: `RentalStatus`, `RentalTrigger`, `RentalStateMachine`, `OvertimeCalculator` (15 phút grace period, làm tròn lên theo giờ), `CancellationPolicy` (trước 24h miễn phí, dưới 24h giữ 20% phí thuê), `SettlementCalculator` (bù trừ cọc và tiền quá giờ), `MoneyRounding` làm tròn VND tại một chỗ duy nhất bằng `MidpointRounding.AwayFromZero`.
- Application: `RentalLifecycleService`, `RentalOptions`, `RentalCancellationOptions`, các ports `IRentalLifecycleRepository`, `IRentalPaymentGate`, `IInvoiceGateway`, `ISubscriptionUsage`, `ILoyaltyAward`, `IRentalEventSink`, `IRentalQueries`.
- Infrastructure: `NpgsqlRentalLifecycleRepository` với `pg_advisory_xact_lock` và `FOR UPDATE` trong transaction chống tranh chấp đồng thời, `RentalPaymentGateAdapter` tạo refund payment có tính lũy nghiệm, `InvoiceGatewayAdapter` tạo hóa đơn quá giờ `kind = 'overtime'`, `NpgsqlRentalQueries` hỗ trợ xem đơn của tôi, đơn vận hành và đơn quá hạn (`overdue`).
- WebApi: `RentalLifecycleController` và `RentalQueriesController` đầy đủ metadata Swagger, cấu hình phân quyền và ánh xạ thông báo qua `ApiSuccessResponseFilter`. Endpoint cũ `PATCH /api/rentals/{id}/status` của `OperationsController` chuyển thành bí danh deprecated với header `Deprecation: true`.
- Gỡ bỏ hoàn cọc tự động tại inspection (`AddInspectionAsync` / O2): Toàn bộ việc quyết toán cọc và hoàn cọc được chuyển tập trung về `complete`.
- Kiểm thử: 100% tests pass (318/318 tests), bao gồm unit test Domain ma trận 6 trạng thái x 5 lệnh, unit test Service, integration test schema/trigger/index, integration test E2E có quá giờ 90 phút, test shortfall, test hủy đơn và test đua tranh `CompleteAsync` 20 lần lặp liên tiếp không flaky. Xem [thay đổi](../changes/2026-10-06-rental-lifecycle.md) và [ADR-004](decisions/ADR-004-rental-lifecycle-and-settlement.md).

## Phân xe, tài xế & Đổi xe (FR-014 / BE-14) — 2026-10-04

Đã hoàn thành module BE-14 theo kiến trúc Clean Architecture:
- Migration `0012_assignment_constraints.sql`: Ràng buộc từ vựng `ck_va_status`, cột sinh `period tsrange` kèm đệm 60 phút, exclusion constraint `ex_va_driver_no_overlap` chống trùng lịch tài xế, các cột lưu vết (`reason`, `note`, `supersedes_id`, `ended_at`, `starts_at`, `ends_at`), và backfill dữ liệu cũ.
- Domain: `AssignmentStatus`, `AssignmentReason`, `DriverEligibility` (kiểm tra hạn bằng GMT+7, active, is_driver, đệm), `RentalAssignmentPolicy`.
- Application: `IAssignmentRepository`, `IAssignmentService`, `IMaintenanceIncidentGateway`, `AssignmentOptions` (cấu hình đệm, cờ kiểm tra hạng bằng), các record/DTO chuẩn.
- Infrastructure: `NpgsqlAssignmentRepository`, `DriverLock` (advisory lock 1002), khóa xe theo thứ tự ID tăng dần chống deadlock, xử lý đổi xe cập nhật `rentals.vehicle_id` dời `vehicle_bookings`, tự động mở phiếu bảo dưỡng sự cố `vehicle_maintenance` khi xe hỏng (`breakdown`/`accident`), ánh xạ lỗi `23P01` -> `DRIVER_BUSY` (409) / `VEHICLE_UNAVAILABLE` (409).
- WebApi: `RentalAssignmentsController` với 5 endpoints chuẩn Swagger metadata, phân quyền `CustomerService`, mapping message trong `ApiSuccessResponseFilter`, endpoint cũ `POST /api/rentals/{id}/assignments` được đánh dấu `[Obsolete]` kèm header `Deprecation: true`.
- Kiểm thử: 100% tests pass bao gồm Domain tests, Schema integration tests, Concurrency 20x loop test không flaky, API contract tests. Xem [thay đổi](../changes/2026-10-04-assignment-vehicle-swap.md) và [ADR-003](decisions/ADR-003-vehicle-and-driver-assignments.md).

## Tìm xe và availability (FR-010 / BE-10) — 2026-10-04

Đã triển khai toàn diện module tìm xe và kiểm tra tính khả dụng theo Clean Architecture:
- Migration `0011_vehicle_bookings_and_availability.sql`: ràng buộc `ck_vehicles_status`, bảng `vehicle_bookings` với gist exclusion constraint (`ex_vehicle_bookings_no_overlap`), trigger đồng bộ từ `hour_rental_detail`, `longterm_rental_contract`, `rentals`, và view `v_vehicle_maintenance_blocks`.
- Domain: `TimeRange` value object với đệm đối xứng 60 phút, `AvailabilityReason`, `BookingStatusRules`.
- Application: `IVehicleSearchRepository`, `IVehicleAvailabilityService`, `IDriverAvailability`, `VehicleSearchService`, `AvailabilityOptions` (`ValidateOnStart`), `BusinessRuleException` với extension `code`.
- Infrastructure: `NpgsqlVehicleSearchRepository` với tìm kiếm filter/sort/paging và index gist, `NpgsqlVehicleAvailabilityService` với advisory lock 1001, `NpgsqlDriverAvailability`.
- WebApi: `VehicleSearchController` (5 endpoints công khai và nội bộ), rate limiter `public-search`, ánh xạ message trong `ApiSuccessResponseFilter`.
- Sửa `CreateRentalAsync`: dùng `VehicleLock.AcquireAsync` + `AssertAvailableAsync` và bắt lỗi `23P01`.
- Kiểm thử: 159 tests pass 100% bao gồm schema tests, integration tests, concurrency tests (đua tranh), và API contract tests. Xem [thay đổi](../changes/2026-10-04-vehicle-availability.md) và [ADR-002](decisions/ADR-002-vehicle-bookings-buffer.md).

## Subscription — 2026-10-03

FR-024 (thuê bằng gói, trừ `remaining_hours`, khóa `FOR UPDATE` chống double deduct) và FR-025 (job hết hạn + nhắc trước hạn, UTC, idempotent) đã hoàn thành. `customer_subscriptions` thêm `reminder_sent_at` (migration `0009`). Integration test cho trừ giờ/chống double deduct và cho job hết hạn. Sửa nốt `[property:]` ở `PlanRequest`/`RefundRequest` sang `[param:]`. **BE-21/FR-023 đã hoàn tất phần thanh toán:** mua gói đi qua PayOS checkout + webhook mới tạo subscription, thêm `GET /api/customer-subscriptions` (migration `0010`). Xem [thay đổi mua gói](../changes/2026-10-03-subscription-plan-payments.md) và [thay đổi trừ giờ/hết hạn](../changes/2026-10-03-subscription-rental-expiry.md).

## Sửa lỗi catalog & envelope — 2026-10-03

`GET /api/vehicles` trả 500 vì hai lỗi độc lập: `42P08` khi tham số lọc NULL (repository) và `InvalidCastException` khi response filter wrap envelope nhưng giữ `ObjectResult.DeclaredType` (WebApi). Đã sửa cả hai, thêm `VehicleSearchTests` + test hồi quy cho filter, và sửa test `SubscriptionExpiryTests` vi phạm `ck_cs_dates`. Xem [thay đổi](../changes/2026-10-03-vehicle-search-envelope-fixes.md).

## RBAC — 2026-10-03

Đã triển khai policy theo specialty, staff/department claims, quyền theo chủ rental/assignment, inspection actor và thu hồi quyền theo authorization version. Migration 0008 đã được kiểm tra trên database test độc lập; 69/69 test pass, tất cả project build thành công. Xem [thay đổi](../changes/2026-10-03-staff-authorization.md) và [hợp đồng frontend](staff-authorization.md). CSRF và các acceptance nghiệp vụ chưa liên quan vẫn giữ trạng thái chưa hoàn thành.

## O2/O4/O5/BE-24 — 2026-10-04
FR-031: licensePlate, ca trực, report staff/driver, Manager giao Maintenance, inspection độc lập tài chính, đổi xe đúng số chỗ/đổi tài khi renting và giữ giá. Migration 0011; PostgreSQL test độc lập. Customer report và tính phí bồi thường tự động chưa triển khai. Xem docs/operational-recovery.md.

Kiểm chứng 2026-10-04: build -warnaserror thành công, 101/101 tests pass trên PostgreSQL 18 tạm; chưa chạy migration trên Neon, chưa kiểm chứng Docker deployment.

## Department assignment — 2026-10-05
FR-007/FR-008: Admin assigns Manager/Staff to departments; Manager assigns Staff only. Dedicated user-ID endpoint preserves specialties, protects multi-role Manager/Admin targets, validates references, and revokes credentials. Manager can read staff/departments; personnel CRUD remains Admin-only. All 112 tests pass against isolated PostgreSQL 18. No new migration. See [change](../changes/2026-10-05-department-assignment.md).

## Facility assignment — 2026-10-05
FR-007/FR-008: Admin assigns Staff/Manager to facilities; Manager assigns Staff only. Department and other employee fields are preserved. Shared organization-assignment logic validates references and protected roles, then invalidates credentials. Verification: 116/116 tests pass with isolated PostgreSQL 18; no application database changes or new migration. See [change](../changes/2026-10-05-facility-assignment.md).
