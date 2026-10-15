# 2026-10-04 — Tìm xe, kiểm tra availability và đệm đối xứng 60 phút (FR-010 / BE-10)

- **FR/NFR:** FR-010 (tìm xe và availability), NFR-001 (clean architecture), NFR-006 (hiệu năng), NFR-007 (testing)
- **Loại:** feature + database migration

Hiện thực toàn diện module tìm xe và kiểm tra tính khả dụng (availability) theo spec `specs/FR-010-search-availability.md`.

## Đã làm

### 1. Cơ sở dữ liệu (Migration `0011_vehicle_bookings_and_availability.sql`)
- Cập nhật ràng buộc `ck_vehicles_status`: `CHECK (status IN ('available', 'renting', 'maintenance', 'inactive'))`.
- Tạo bảng `vehicle_bookings` với cột generated `period tsrange GENERATED ALWAYS AS (tsrange(starts_at, ends_at + make_interval(mins => buffer_minutes), '[)')) STORED`.
- Ràng buộc loại trừ chống trùng lấn: `CONSTRAINT ex_vehicle_bookings_no_overlap EXCLUDE USING gist (vehicle_id WITH =, period WITH &&) WHERE (is_blocking)`.
- Trigger tự động đồng bộ:
  - `fn_sync_vehicle_booking_from_detail` trên `hour_rental_detail` và `longterm_rental_contract`.
  - `fn_sync_vehicle_booking_from_rental` trên `rentals` (xử lý đổi trạng thái và đổi xe an toàn).
- Tạo view `v_vehicle_maintenance_blocks` chặn xe khi bảo dưỡng ở trạng thái `open`, `scheduled`, `in_progress`, hoặc `completed` mà `is_safe = false`.
- Backfill dữ liệu cũ từ `rentals` vào `vehicle_bookings` an toàn với `ON CONFLICT DO NOTHING`.

### 2. Tầng Domain
- `TimeRange`: Value object đại diện cho khoảng thời gian `[From, To)` nửa mở, kiểm tra hợp lệ, `Overlaps`, `WithBuffer`, và `ConflictsWith`.
- `AvailabilityReason`: Enum lý do không khả dụng (`Booked`, `Maintenance`, `Inactive`, `NotFound`).
- `BookingStatusRules`: Quy tắc xác định trạng thái thuê có đang giữ chỗ chặn xe hay không (`pending`, `pre_inspection`, `renting`, `post_inspection`).

### 3. Tầng Application
- Định nghĩa các hợp đồng và DTOs: `VehicleSearchQuery`, `VehicleSearchResult`, `AvailabilityResult`, `BusySlot`, `ActiveRentalPolicy`, `AffectedRental`.
- Ports: `IVehicleSearchRepository`, `IVehicleAvailabilityService`, `IDriverAvailability`.
- Service: `VehicleSearchService` với các quy tắc validation cho khoảng thời gian (tối đa 366 ngày), sắp xếp hợp lệ (`recommended`, `price_asc`, `price_desc`, `newest`), phân trang.
- Quản lý lỗi: `BusinessRuleException(code, message, statusCode)`, `ConflictException(409)`, `NotFoundException(404)`, `ForbiddenException(403)`.
- Mở rộng middleware `UseExceptionHandler` trong WebApi để gắn `ProblemDetails.Extensions["code"]`.
- `AvailabilityOptions` hỗ trợ cấu hình đệm (mặc định 60 phút) với `ValidateOnStart`.

### 4. Tầng Infrastructure
- `VehicleLock`: Helper quản lý PostgreSQL transactional advisory lock `pg_advisory_xact_lock(1001, vehicleId)` chống race condition khi tạo rental hoặc đổi xe.
- `VehicleAvailabilityChecks`: Đánh giá availability dựa trên trạng thái xe, bảng `vehicle_bookings` (với đệm đối xứng 60 phút) và view bảo dưỡng `v_vehicle_maintenance_blocks`.
- Chuyển đổi timestamp Npgsql 8.0: Đảm bảo tham số luôn dùng `DateTimeKind.Unspecified` để tránh lỗi với cột `timestamp`.
- `NpgsqlVehicleSearchRepository`: Truy vấn tìm kiếm tối ưu với bộ lọc, đếm tổng bằng `COUNT(*) OVER()`, sắp xếp tie-breaker theo `v.id`, và LEFT JOIN chi tiết không gây N+1.
- `NpgsqlVehicleAvailabilityService`: Triển khai `IVehicleAvailabilityService` (`CheckAsync` và `AssertAvailableAsync`).
- `NpgsqlDriverAvailability`: Triển khai `IDriverAvailability` sẵn sàng tích hợp với FR-014.

### 5. Tầng WebApi
- `VehicleSearchController`:
  - `GET /api/vehicles/search`: Công khai (`[AllowAnonymous]`), rate limiting `public-search` (60 req/phút), Cache-Control thích ứng (`no-store` khi có khoảng ngày, `public, max-age=30` khi không có ngày).
  - `GET /api/vehicles/{id}/availability`: Công khai, kiểm tra availability theo khoảng thời gian.
  - `GET /api/vehicles/{id}/busy-slots`: Công khai, chỉ trả về các khoảng bận (`StartsAt`, `EndsAt`, `Reason`), bảo vệ quyền riêng tư, tuyệt đối không lộ thông tin khách hàng hay rental ID.
  - `GET /api/vehicles/vehicle-types/{id}/active-policy`: Công khai, lấy chính sách thuê hiện hành của loại xe.
  - `GET /api/vehicles/{id}/affected-rentals`: Bảo vệ bằng phân quyền (`Roles = "Admin,Manager,Staff,CustomerService,Maintenance"`), liệt kê các rental bị ảnh hưởng khi xe cần bảo dưỡng hoặc sửa đổi.
- `ApiSuccessResponseFilter`: Bổ sung mapping message chuẩn cho cả 5 actions của `VehicleSearchController`.

### 6. Sửa `CreateRentalAsync` (Phòng chống Race Condition)
- Tích hợp `VehicleLock.AcquireAsync(tx, vehicleId)` bên trong transaction.
- Gọi `VehicleAvailabilityChecks.EvaluateAsync` và `ThrowIfBlocked` trước khi INSERT.
- Bắt lỗi `PostgresException` mã `23P01` (exclusion violation) để quăng `ConflictException("VEHICLE_UNAVAILABLE")` (409) như lưới an toàn cuối cùng.
- Sử dụng `TimeProvider` để sinh mã hóa đơn, loại bỏ hoàn toàn `DateTime.UtcNow` trực tiếp.

## Test
- 159 tests pass 100%:
  - `VehicleBookingSchemaTests` (14 tests): kiểm tra exclusion constraint, đệm 59/60/61 phút, trigger sync từ hour/longterm detail, và maintenance view.
  - `VehicleBookingMigrationTests` (2 tests): migration chạy thành công trên DB trống và DB có dữ liệu; chặn migration khi status xe không hợp lệ.
  - `DomainAvailabilityTests` (19 tests): `TimeRange` và quy tắc `BookingStatusRules`.
  - `VehicleSearchServiceTests` (14 tests): validation query, sort, date range, mã lỗi.
  - `VehicleSearchIntegrationTests` (8 tests): test tìm kiếm thực tế trên PostgreSQL Neon với filter, sort, paging, UTC contract.
  - `VehicleConcurrencyTests` (3 tests): đua tranh tạo rental đồng thời trên 1 xe (1 thành công, 1 nhận 409), vi phạm đệm 60 phút nhận 409, exclusion constraint bắt INSERT lách lock (`23P01`).
  - `VehicleSearchApiContractTests` (7 tests): Swagger metadata, route, privacy ở busy-slots, message mapping trong `ApiSuccessResponseFilter`, `BusinessRuleException`.
