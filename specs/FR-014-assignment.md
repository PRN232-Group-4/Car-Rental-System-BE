<<<<<<< HEAD
# FR-014 — Phân xe / tài xế (BE-14)

- **Status:** In progress; **Priority:** P0; **Milestone:** M4; **Assignee:** Huy
- **Phụ thuộc:** BE-10 (vehicle_bookings, khóa xe, `IVehicleAvailabilityService` — **làm trước**), BE-08 (Done), BE-12 (Lạc, tạo rental), BE-15/16 (kiểm định). **Phục vụ:** BE-33 (Lạc, lịch tài xế), FE-15 (Trí), FE-24 (Huy), BE-17.
- **Migration dự kiến:** `0009_assignment_constraints.sql`.

## 1. Hiện trạng (khảo sát)

Đã có: `POST /api/rentals/{id}/assignments {driverStaffId}` (Admin/Manager/Staff) với transaction + advisory lock, yêu cầu pre-inspection đạt, kiểm tra tài xế `is_driver`/active/bằng lái còn hạn/không trùng lịch, chuyển bản ghi cũ `assigned → reassigned`, lưu `assigned_by`; `GET /api/drivers/schedule`. Index `ux_vehicle_assignments_active_rental (rental_id) WHERE status='assigned'`.

Còn thiếu: **đổi xe**; ràng buộc CSDL cho TODO-01 ("một xe một tài" theo thời gian) — hiện chỉ khóa theo `rental_id`; từ vựng `status` không có CHECK; không có bản ghi cho khách tự lái; không có lý do/ghi chú đổi; không có API liệt kê tài xế/xe thay thế hợp lệ.

## 2. Quyết định đã chốt

| # | Quyết định | Nguồn |
|---|---|---|
| D1 | **Hai endpoint riêng**: đổi tài xế (`/assignments/driver`) và đổi xe (`/assignments/vehicle`); endpoint cũ `POST /assignments` giữ làm bí danh **deprecated** của `/driver` | Huy |
| D2 | `rentals.vehicle_id` luôn là **xe hiện hành**; đổi xe cập nhật cột này (trigger của BE-10 đồng bộ `vehicle_bookings`); lịch sử ở `vehicle_assignments` | Thiết kế |
| D3 | Đổi xe chỉ cho **cùng `vehicle_type_id`**; khác loại → 409 `VEHICLE_TYPE_MISMATCH` (tính lại chênh lệch giá cần quyết định với BE-12, câu hỏi O2) | Thiết kế |
| D4 | Từ vựng `status`: `assigned` (hiện hành), `reassigned` (bị thay), `completed`, `cancelled` | Mã hiện có + SRS |
| D5 | Mọi rental có đúng một bản ghi `assigned` sau khi qua pre-inspection; khách tự lái dùng `driver_staff_id = NULL` | SRS UC-01 |

## 3. Hợp đồng API

Controller mới `RentalAssignmentsController`. Quyền: **Admin, Manager, Staff** (ghi); đọc lịch sử cùng nhóm này. Ghi `assigned_by` = email người thực hiện.

| Method | Path | Mô tả |
|---|---|---|
| GET | `/api/rentals/{id}/assignments` | Lịch sử gán của đơn (mới nhất trước) |
| POST | `/api/rentals/{id}/assignments/driver` | Gán hoặc **đổi tài xế** |
| POST | `/api/rentals/{id}/assignments/vehicle` | **Đổi xe** (xe hỏng/tai nạn/yêu cầu khách) |
| GET | `/api/rentals/{id}/assignable-drivers` | Tài xế hợp lệ cho đơn, kèm lý do loại |
| GET | `/api/rentals/{id}/assignable-vehicles` | Xe thay thế hợp lệ (cùng loại, trống phần còn lại) |
| GET | `/api/drivers/schedule?from&to&driverStaffId` | Giữ nguyên; thêm `rentalId`, `vehicleId`, `status` vào mỗi dòng nếu thiếu |
| POST | `/api/rentals/{id}/assignments` | **Deprecated**: ủy quyền sang `/driver`; trả header `Deprecation: true` |

### 3.1 `POST …/assignments/driver`

```json
{ "driverStaffId": 123, "reason": "initial" , "note": "Tài xế chính nghỉ ốm" }
```

* `driverStaffId` bắt buộc khi đơn thuê theo giờ có `has_driver = true`; `null` hợp lệ **chỉ** khi `has_driver = false` (khách tự lái; khi đó yêu cầu hồ sơ khách `license_status = 'approved'` và bằng chưa hết hạn đến hết khoảng thuê → 409 `CUSTOMER_LICENSE_NOT_VALID`).
* `reason`: `initial | driver_unavailable | driver_request | customer_request | other` (mặc định `initial` khi chưa có bản ghi `assigned`, ngược lại bắt buộc). `note` <= 500 ký tự.
* Điều kiện đơn: `rentals.status IN ('pre_inspection','renting')` (pending → 409 `ILLEGAL_RENTAL_STATE`) và có pre-inspection đạt (giữ quy tắc hiện có).
* Điều kiện tài xế (lỗi → 409 `DRIVER_NOT_ELIGIBLE` kèm `reasons[]`): staff `is_driver`; user `is_active`; bằng lái còn hạn đến hết ngày kết thúc theo GMT+7; không có bản ghi `assigned` khác trùng khoảng `[start, end + đệm tài xế)`; cổng `IDriverAvailability` (BE-33 sẽ cắm ca làm/ngày nghỉ/khu vực). Kiểm tra hạng bằng lái theo loại xe **mặc định tắt** (`Assignment:LicenseClassRules:Enabled=false`) vì bảng hạng bằng sau luật mới chưa được xác nhận — câu hỏi O3.
* Thành công `200`: bản ghi `assigned` mới; bản ghi trước chuyển `reassigned` (kèm `ended_at`, `ends_at` cắt tại thời điểm đổi); tự gán `supersedes_id`.
* Lỗi tranh chấp: `409 DRIVER_BUSY` khi vi phạm exclusion của tài xế (SQLSTATE 23P01).

### 3.2 `POST …/assignments/vehicle`

```json
{ "newVehicleId": 45, "reason": "breakdown", "note": "Nổ lốp trên QL1", "keepDriver": true }
```

* `reason`: `breakdown | accident | maintenance | customer_request | other` (bắt buộc).
* Điều kiện đơn: `rentals.status IN ('pre_inspection','renting')`.
* Xe mới: tồn tại; `status IN ('available','renting')`; **cùng `vehicle_type_id`** với xe hiện tại; trống cho `[max(now, start), end + đệm)` theo `IVehicleAvailabilityService` (loại trừ chính đơn này). Lỗi: 409 `VEHICLE_UNAVAILABLE`, `VEHICLE_TYPE_MISMATCH`.
* Trong **một transaction**, khóa advisory xe cũ và xe mới theo thứ tự id tăng dần, rồi: (1) `UPDATE rentals SET vehicle_id = @new` (trigger của BE-10 dời `vehicle_bookings`, exclusion là lưới cuối); (2) bản ghi `assigned` hiện tại → `reassigned`; (3) tạo bản ghi `assigned` mới với xe mới, giữ tài xế nếu `keepDriver`; (4) nếu `reason IN ('breakdown','accident')`: xe cũ → `status='maintenance'`, `is_renting=false` và **mở phiếu bảo dưỡng** (`maintenance_type='incident'`, `description` = lý do, `rental_id` = đơn) qua cổng của module bảo trì (BE-24 đã Done: tái dùng, không viết SQL song song nếu đã có service); (5) xe mới → `is_renting = true` nếu đơn đang `renting`; (6) ghi `rental_status_history` (khi FR-018 đã merge) hoặc log có cấu trúc.
* Không có Inspection tự động; response trả `requiresInspection: true` để FE-15 hướng dẫn kiểm định nhanh xe thay thế. (Giới hạn `UNIQUE(rental_id, inspection_type)` của SRS UC-03 là việc của BE-15/16: đề xuất thêm `assignment_id` vào `inspections` — câu hỏi O4.)
* Đổi xe cho đơn có tài xế: tài xế **không đổi**; giữ nguyên khoảng giữ chỗ của tài xế.

### 3.3 `assignable-drivers` / `assignable-vehicles`

* `assignable-drivers` → `[{ staffId, fullName, avatar, rating, licenseExpiry, eligible, reasons: ["LICENSE_EXPIRING","BUSY","INACTIVE", …] }]` (gồm cả người không hợp lệ kèm lý do để staff hiểu; lọc `eligibleOnly` mặc định `true`).
* `assignable-vehicles` → danh sách xe cùng loại, trống phần còn lại, ưu tiên cùng chi nhánh, tối đa 50, sắp theo (cùng chi nhánh, năm sản xuất mới).

## 4. Ràng buộc "một xe một tài" (TODO-01) — cách đáp ứng

| Yêu cầu | Cơ chế |
|---|---|
| Một xe không có hai lượt chồng giờ | `ex_vehicle_bookings_no_overlap` (BE-10) |
| Một đơn chỉ có một bản ghi `assigned` | Index sẵn có `ux_vehicle_assignments_active_rental` |
| Một tài xế không bị gán hai đơn chồng giờ (kể cả đệm) | Exclusion mới `ex_va_driver_no_overlap` (dưới) |
| Bản ghi gán khớp xe của đơn | Kiểm tra trong ứng dụng + integration test bất biến (không dùng trigger để tránh phụ thuộc thứ tự cập nhật) |

## 5. Migration `0009`

Tham chiếu (phải chạy thử trên PostgreSQL 16 và có integration test):

```sql
DO $$
DECLARE bad INTEGER;
BEGIN
  SELECT count(*) INTO bad FROM vehicle_assignments
   WHERE status IS NULL OR status NOT IN ('assigned','reassigned','completed','cancelled');
  IF bad > 0 THEN RAISE EXCEPTION 'vehicle_assignments.status có % dòng ngoài tập hợp lệ', bad; END IF;
END $$;
ALTER TABLE vehicle_assignments ALTER COLUMN status SET NOT NULL;
ALTER TABLE vehicle_assignments ADD CONSTRAINT ck_va_status
  CHECK (status IN ('assigned','reassigned','completed','cancelled'));

ALTER TABLE vehicle_assignments
  ADD COLUMN IF NOT EXISTS reason          VARCHAR(30),
  ADD COLUMN IF NOT EXISTS note            VARCHAR(500),
  ADD COLUMN IF NOT EXISTS supersedes_id   INTEGER,
  ADD COLUMN IF NOT EXISTS ended_at        TIMESTAMP,
  ADD COLUMN IF NOT EXISTS starts_at       TIMESTAMP,
  ADD COLUMN IF NOT EXISTS ends_at         TIMESTAMP,
  ADD COLUMN IF NOT EXISTS buffer_minutes  INTEGER NOT NULL DEFAULT 60;

-- Backfill starts_at/ends_at từ chi tiết đơn (hour_rental_detail hoặc longterm_rental_contract)
UPDATE vehicle_assignments va SET
  starts_at = COALESCE((SELECT start_time FROM hour_rental_detail WHERE rental_id = va.rental_id),
                       (SELECT start_time FROM longterm_rental_contract WHERE rental_id = va.rental_id)),
  ends_at   = COALESCE((SELECT end_time   FROM hour_rental_detail WHERE rental_id = va.rental_id),
                       (SELECT end_time   FROM longterm_rental_contract WHERE rental_id = va.rental_id))
 WHERE va.starts_at IS NULL;

DO $$
DECLARE bad INTEGER;
BEGIN
  SELECT count(*) INTO bad FROM vehicle_assignments
   WHERE status = 'assigned' AND driver_staff_id IS NOT NULL AND (starts_at IS NULL OR ends_at IS NULL OR ends_at <= starts_at);
  IF bad > 0 THEN RAISE EXCEPTION '% bản ghi assigned có tài xế nhưng thiếu khoảng giờ hợp lệ', bad; END IF;
END $$;

ALTER TABLE vehicle_assignments ADD COLUMN IF NOT EXISTS period tsrange
  GENERATED ALWAYS AS (CASE WHEN starts_at IS NOT NULL AND ends_at IS NOT NULL AND ends_at > starts_at
                            THEN tsrange(starts_at, ends_at + make_interval(mins => buffer_minutes), '[)') END) STORED;

ALTER TABLE vehicle_assignments ADD CONSTRAINT ex_va_driver_no_overlap
  EXCLUDE USING gist (driver_staff_id WITH =, period WITH &&)
  WHERE (status = 'assigned' AND driver_staff_id IS NOT NULL AND period IS NOT NULL);

CREATE INDEX IF NOT EXISTS ix_va_driver_status ON vehicle_assignments (driver_staff_id, status);
CREATE INDEX IF NOT EXISTS ix_va_vehicle ON vehicle_assignments (vehicle_id, status);
```

Khi tạo bản ghi mới, ứng dụng ghi `starts_at/ends_at` = khoảng **còn lại** của đơn (`max(now, start)`..`end`) cho đổi tài xế/đổi xe giữa chừng, hoặc toàn bộ khoảng đơn cho gán ban đầu. Bản ghi `reassigned` được cắt `ends_at = thời điểm đổi` để lịch sử phản ánh đúng ai chạy khi nào.

## 6. Kiến trúc & mã nguồn

* Domain: `AssignmentStatus` (enum + chuỗi), `AssignmentReason`, `DriverEligibility` (quy tắc thuần: active, `is_driver`, hạn bằng, kết quả danh sách `reasons`), `RentalAssignmentPolicy` (đơn ở trạng thái nào được gán/đổi).
* Application: `IAssignmentRepository`, `IAssignmentService` (`AssignDriverAsync`, `ReplaceVehicleAsync`, `ListAssignableDriversAsync`, `ListAssignableVehiclesAsync`), `IDriverAvailability` (triển khai mặc định: không trùng + bằng; **BE-33 cắm thêm** ca làm/ngày nghỉ/khu vực mà không đổi API), `IMaintenanceIncidentGateway`.
* Infrastructure: `NpgsqlAssignmentRepository` (transaction, khóa advisory `1001` xe / `1002` tài xế, ánh xạ `23P01` → `DRIVER_BUSY`/`VEHICLE_UNAVAILABLE`).
* WebApi: `RentalAssignmentsController`; DTO; Swagger đầy đủ; ánh xạ message trong `ApiSuccessResponseFilter` cho mọi action mới.
* Di chuyển logic hiện nằm trong `NpgsqlOperationsRepository.AssignAsync` sang service/repository mới; giữ `AssignAsync` làm lớp mỏng gọi service để không vỡ test cũ (`DriverAssignmentTests`), sau đó cập nhật test sang API mới.
* Mã lỗi: `ILLEGAL_RENTAL_STATE`, `PRE_INSPECTION_REQUIRED`, `DRIVER_NOT_ELIGIBLE`, `DRIVER_BUSY`, `DRIVER_REQUIRED`, `DRIVER_NOT_ALLOWED_FOR_SELF_DRIVE`, `CUSTOMER_LICENSE_NOT_VALID`, `VEHICLE_UNAVAILABLE`, `VEHICLE_TYPE_MISMATCH`, `RENTAL_NOT_FOUND`.

## 7. Kiểm thử (tối thiểu)

* **Domain:** `DriverEligibility` mọi nhánh lý do; `RentalAssignmentPolicy` theo từng trạng thái đơn.
* **Integration:** (1) gán lần đầu (có tài xế, tự lái); (2) đổi tài xế: bản ghi cũ `reassigned` + `ends_at` cắt đúng, bản mới `assigned`, `supersedes_id`; (3) tài xế trùng giờ → 409 `DRIVER_BUSY`; hai request song song → đúng một thắng (cả khi bỏ advisory lock, exclusion chặn); (4) đổi xe thành công: `rentals.vehicle_id` đổi, `vehicle_bookings` dời, phần quá khứ của xe mới không gây xung đột oan; (5) đổi xe khác loại → 409; xe mới bận → 409; (6) lý do `breakdown` → xe cũ `maintenance` + có phiếu bảo dưỡng + xe cũ biến mất khỏi `search`; (7) rollback toàn bộ khi bất kỳ bước nào lỗi (không còn trạng thái nửa vời); (8) bất biến "assignment.vehicle_id = rentals.vehicle_id với bản ghi assigned" sau mọi thao tác; (9) `assignable-drivers`/`assignable-vehicles` đúng lý do loại; (10) endpoint cũ vẫn chạy + header `Deprecation`.
* **API:** phân quyền (Customer 403, ẩn danh 401), validation 400, Swagger, message mapping.

## 8. Acceptance criteria

- [x] Hai endpoint đổi tài xế / đổi xe tách biệt; endpoint cũ deprecated hoạt động
- [x] Đổi xe: cập nhật `rentals.vehicle_id`, `vehicle_bookings`, assignment, bảo dưỡng xe cũ (nếu hỏng) trong một transaction
- [x] Tài xế không thể bị gán hai khoảng chồng nhau (kể cả đua tranh) — có exclusion ở CSDL
- [x] CHECK từ vựng `status`; migration `0012` chạy trên DB trống và DB có dữ liệu
- [x] Khách tự lái có bản ghi `driver_staff_id = NULL` và kiểm tra GPLX
- [x] `IDriverAvailability` là điểm cắm cho BE-33; không có phụ thuộc vòng với BE-33
- [x] Swagger + message mapping + tài liệu (`docs/progress.md`, `changes/…-assignment-vehicle-swap.md`, CHANGELOG, open-questions, ADR "rentals.vehicle_id là xe hiện hành")

## 9. Câu hỏi mở / giả định

* **O1.** Đệm tài xế mặc định 60 phút (di chuyển/nghỉ) — xác nhận.
* **O2.** Đổi xe khác loại và tính lại chênh lệch giá: cần quyết định với BE-12; hiện chặn.
* **O3.** Bảng hạng GPLX theo loại xe (luật mới): chờ xác nhận nghiệp vụ; cờ mặc định tắt.
* **O4.** `inspections` cần `assignment_id` để kiểm định xe thay thế không đụng `UNIQUE(rental_id, inspection_type)` — đề xuất cho BE-15/16 (Lạc/Anh).
* **O5.** Phụ thuộc vòng trong kế hoạch: BE-33 (tuần 6) ghi phụ thuộc BE-14 (tuần 7). Giải bằng cổng `IDriverAvailability`; cần Lạc xác nhận.

---

## Prompt thực thi (Claude Code — repo BE)

```text
Bạn là senior .NET/PostgreSQL engineer. Hiện thực BE-14 (FR-014) theo ĐÚNG specs/FR-014-assignment.md (đã cập nhật). Điều kiện tiên quyết: BE-10 đã merge (vehicle_bookings, VehicleLock, IVehicleAvailabilityService). Nếu chưa, DỪNG và báo. Nhánh feat/be-14-assignment từ main mới nhất; kiểm tra số migration (0009 có thể phải đổi).

ĐỌC TRƯỚC: agent.md; docs/architecture.md; docs/business-rules.md; db/migrations/README.md (và 0006, 0007); specs/FR-014, FR-010, FR-015, FR-016, FR-018, FR-026; NpgsqlOperationsRepository.AssignAsync + ListDriverScheduleAsync + AddInspectionAsync; OperationsController.Assign/DriverSchedule; tests/DriverAssignmentTests.cs; module bảo trì BE-24 (Kiệt, Done): tìm service/repository mở phiếu vehicle_maintenance để TÁI DÙNG.

QUY TẮC: như agent.md. Không phá hợp đồng endpoint cũ (giữ alias deprecated). Không dùng DateTime.UtcNow. Khóa advisory theo thứ tự id tăng dần. Giữ nguyên các test cũ cho tới khi test mới thay thế được chúng.

THỨ TỰ: (1) migration 0009 + test schema/exclusion; (2) Domain; (3) Application (service, ports, IDriverAvailability mặc định); (4) Infrastructure (repo mới, chuyển logic từ AssignAsync, ánh xạ 23P01); (5) WebApi (controller, DTO, Swagger, message mapping, header Deprecation cho alias); (6) test mục 7; (7) tài liệu mục 8.

KIỂM CHỨNG và dán kết quả thật: dotnet build -warnaserror; dotnet test; test đua tranh (2 request song song) chạy 20 lần liên tiếp không flaky; migration up trên DB trống + DB có dữ liệu cũ (có assignment hiện hữu).

BÁO CÁO: file đổi, kết quả lệnh, giả định đã ghi, và các điểm cần Lạc (BE-12/33), Anh/Lạc (BE-15/16 inspections.assignment_id), Kiệt (bảo trì) xác nhận. Gặp mâu thuẫn giữa spec và code: dừng và hỏi, không tự chọn.
```

---
*Ghi chú:* Xem thêm [FR-031](FR-031-operational-recovery.md) và [API frontend](../docs/operational-recovery.md) về quy trình phục hồi sự cố / thay xe của Lạc.
