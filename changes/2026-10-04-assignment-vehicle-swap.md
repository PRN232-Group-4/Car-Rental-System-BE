# 2026-10-04 — Phân xe, tài xế & Đổi xe (FR-014 / BE-14)

- **FR/NFR:** FR-014 (phân xe/tài xế & đổi xe), NFR-001 (clean architecture), NFR-006 (hiệu năng), NFR-007 (testing)
- **Loại:** feature + database migration

Hiện thực toàn diện module phân xe, tài xế và đổi xe (swap vehicle) theo spec `specs/FR-014-assignment.md`.

## Đã làm

### 1. Cơ sở dữ liệu (Migration `0012_assignment_constraints.sql`)
- Chuẩn hóa từ vựng `vehicle_assignments.status` qua ràng buộc `ck_va_status`: `CHECK (status IN ('assigned', 'reassigned', 'completed', 'cancelled'))`.
- Bổ sung các cột theo dõi và lên lịch: `reason` (VARCHAR 30), `note` (VARCHAR 500), `supersedes_id` (INTEGER), `ended_at` (TIMESTAMP), `starts_at` (TIMESTAMP), `ends_at` (TIMESTAMP), `buffer_minutes` (INTEGER NOT NULL DEFAULT 60).
- Backfill tự động `starts_at` và `ends_at` cho các bản ghi cũ từ `hour_rental_detail` hoặc `longterm_rental_contract`.
- Cột generated `period tsrange GENERATED ALWAYS AS (CASE WHEN starts_at IS NOT NULL AND ends_at IS NOT NULL AND ends_at > starts_at THEN tsrange(starts_at, ends_at + make_interval(mins => buffer_minutes), '[)') END) STORED`.
- Ràng buộc loại trừ chống trùng lấn lịch tài xế:
  `CONSTRAINT ex_va_driver_no_overlap EXCLUDE USING gist (driver_staff_id WITH =, period WITH &&) WHERE (status = 'assigned' AND driver_staff_id IS NOT NULL AND period IS NOT NULL)`.
- Tạo các index hiệu năng: `ix_va_driver_status` và `ix_va_vehicle`.

### 2. Tầng Domain
- `AssignmentStatus`: Định nghĩa hằng chuỗi và tập hợp trạng thái hợp lệ (`assigned`, `reassigned`, `completed`, `cancelled`).
- `AssignmentReason`: Định nghĩa các lý do gán/đổi xe (`initial`, `breakdown`, `accident`, `maintenance`, `driver_unavailable`, `driver_request`, `customer_request`, `other`), nhận diện sự cố kỹ thuật qua `IsIncident(...)`.
- `DriverEligibility`: Đánh giá tính hợp lệ của tài xế độc lập không phụ thuộc I/O (nhân viên lái xe `is_driver`, tài khoản hoạt động `is_active`, hạn bằng lái theo GMT+7 đối chiếu với thời điểm kết thúc thuê, kiểm tra hạng bằng theo cờ cấu hình).
- `RentalAssignmentPolicy`: Quy tắc xác định trạng thái đơn thuê được phép gán hoặc đổi xe/tài xế (`pre_inspection`, `renting`).

### 3. Tầng Application
- Records & DTOs: `DriverAssignmentCommand`, `VehicleReplacementCommand`, `AssignmentResponse`, `VehicleReplacementResult`, `AssignableDriverItem`, `AssignableVehicleItem`, `RentalScheduleInfo`.
- Ports: `IAssignmentRepository`, `IAssignmentService`, `IMaintenanceIncidentGateway`.
- Service `AssignmentService`:
  - `AssignDriverAsync`: Kiểm tra điều kiện đơn thuê, pre-inspection, phân biệt đơn có tài xế vs khách tự lái (yêu cầu hồ sơ khách có `license_status = 'approved'` và bằng còn hạn đến hết chuyến), gán tài xế hoặc cập nhật tài xế mới.
  - `ReplaceVehicleAsync`: Đổi xe cùng loại (`vehicle_type_id`), kiểm tra xe mới còn trống qua `IVehicleAvailabilityService`, dời assignment, xử lý sự cố xe cũ và mở phiếu bảo dưỡng nếu có sự cố.
  - `ListAssignableDriversAsync`: Liệt kê tài xế kèm cờ `eligible` và danh sách chi tiết các lý do không hợp lệ nếu có (`NOT_ACTIVE`, `NOT_A_DRIVER`, `LICENSE_EXPIRED`, `SCHEDULE_CONFLICT`).
  - `ListAssignableVehiclesAsync`: Liệt kê xe cùng loại còn trống phần thời gian còn lại của đơn, ưu tiên cùng cơ sở và năm sản xuất mới nhất.
  - `GetAssignmentsAsync`: Lấy lịch sử gán của đơn thuê.
- `AssignmentOptions`: Cấu hình đệm tài xế (mặc định 60 phút) và cờ kiểm tra hạng bằng (`CheckLicenseClass = false`).

### 4. Tầng Infrastructure
- `DriverLock`: Quản lý PostgreSQL transactional advisory lock `pg_advisory_xact_lock(1002, driverStaffId)` chống race condition khi gán cùng tài xế.
- Cơ chế khóa xe chống deadlock: Khóa advisory lock category `1001` theo thứ tự ID tăng dần (`Math.Min(oldId, newId)` trước, `Math.Max(oldId, newId)` sau).
- `NpgsqlAssignmentRepository`:
  - Trong cùng một database transaction: Khóa tài nguyên -> Cập nhật `rentals.vehicle_id` (kích hoạt trigger BE-10 để dời `vehicle_bookings`) -> Đóng bản ghi assignment cũ với `status = 'reassigned'`, `ended_at = now`, `ends_at = LEAST(ends_at, now)` -> Thêm bản ghi assignment mới với `supersedes_id`.
  - Mở phiếu bảo dưỡng sự cố qua `IMaintenanceIncidentGateway` khi lý do đổi là `breakdown` hoặc `accident`.
  - Ánh xạ mã lỗi PostgreSQL `23P01` (exclusion constraint violation) thành `ConflictException("DRIVER_BUSY")` cho tài xế và `ConflictException("VEHICLE_UNAVAILABLE")` cho xe.
- Giữ tương thích ngược trong `NpgsqlOperationsRepository.AssignAsync`: Bổ sung `starts_at`, `ends_at`, `reason` khi insert để kích hoạt ràng buộc exclusion ngay cả với luồng cũ.

### 5. Tầng WebApi
- `RentalAssignmentsController`:
  - `POST /api/rentals/{id}/assignments/driver`: Gán hoặc đổi tài xế, yêu cầu quyền `CustomerService`.
  - `POST /api/rentals/{id}/assignments/vehicle`: Đổi xe, yêu cầu quyền `CustomerService`.
  - `GET /api/rentals/{id}/assignments`: Lấy lịch sử gán xe/tài xế.
  - `GET /api/rentals/{id}/assignable-drivers`: Lấy danh sách tài xế hợp lệ/không hợp lệ kèm lý do.
  - `GET /api/rentals/{id}/assignable-vehicles`: Lấy danh sách xe thay thế hợp lệ.
- Hỗ trợ Deprecation:
  - Giữ nguyên endpoint cũ `POST /api/rentals/{id}/assignments` trong `OperationsController`.
  - Đánh dấu `[Obsolete]` và gửi header `Deprecation: true`.
- `ApiSuccessResponseFilter`: Thêm mapping message thành công cho cả 5 actions của `RentalAssignmentsController`.

### 6. Kiểm thử
- `DomainAssignmentTests` (27 tests): Kiểm tra toàn diện `DriverEligibility`, `RentalAssignmentPolicy`, và `AssignmentReason`.
- `AssignmentSchemaTests` (10 tests): Ràng buộc từ vựng `ck_va_status`, exclusion constraint với đệm 59/60/61 phút, idempotency của migration.
- `AssignmentServiceTests` (11 tests): Kiểm tra nghiệp vụ application, các mã lỗi `ILLEGAL_RENTAL_STATE`, `PRE_INSPECTION_REQUIRED`, `DRIVER_REQUIRED`, `CUSTOMER_LICENSE_NOT_VALID`, `VEHICLE_TYPE_MISMATCH`.
- `AssignmentIntegrationTests` (6 tests): Kiểm tra end-to-end trên PostgreSQL Neon thật với advisory lock, đổi xe duy trì bất biến `rentals.vehicle_id == assignment.vehicle_id`, mở phiếu bảo dưỡng khi sự cố, và kiểm tra loại trừ xe hiện tại.
- `DriverAssignmentConcurrencyTests` (1 test): Vòng lặp 20 lần liên tiếp chạy 2 transaction đua tranh gán cùng 1 tài xế; trong tất cả 20 lần đều chứng minh 1 transaction thắng và 1 transaction nhận 409 `DRIVER_BUSY` mà không bị flaky.
- `RentalAssignmentsApiContractTests` (14 tests): Kiểm tra hợp đồng API, route, phân quyền, Swagger ProducesResponseType, obsolete/deprecation, và message wrapper.
