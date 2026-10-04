# FR-010 — Tìm xe và availability (BE-10)

- **Status:** In progress; **Priority:** P0; **Milestone:** M3; **Assignee:** Huy
- **Phụ thuộc:** BE-09 (vehicles, Done), BE-11 (rental_policies, Done). **Phục vụ:** FE-04 (Huy), BE-12 (Lạc, tạo rental), BE-14, BE-17.
- **Migration dự kiến:** `0008_vehicle_bookings_and_availability.sql` (đổi số nếu trùng khi merge; xem `db/migrations/README.md`).

## 1. Mục tiêu

1. Tìm xe **khả dụng** theo thời gian/chi nhánh/loại/giá/thông số, công khai (không cần đăng nhập), có phân trang, sắp xếp, nhanh.
2. Cung cấp **một nguồn sự thật duy nhất** cho "xe có trống không" (`vehicle_bookings`) dùng chung cho tìm kiếm, tạo rental, đổi xe.
3. **Chặn double-booking triệt để** ở hai lớp: advisory lock + kiểm tra lại trong transaction (lớp ứng dụng) và exclusion constraint (lớp CSDL, TODO-02 của SRS).

## 2. Quyết định đã chốt

| # | Quyết định | Nguồn |
|---|---|---|
| D1 | Đệm **60 phút** giữa hai lượt thuê của cùng một xe, đối xứng (hai khoảng thuê cách nhau < 60 phút là xung đột). Xe hư hại / không vận hành: chuyển status `maintenance`, **không** dùng đệm | Huy |
| D2 | Dùng **advisory lock** trên `vehicle_id` trong transaction tạo rental, kèm kiểm tra lại; thêm exclusion constraint làm lưới an toàn | Huy + thiết kế |
| D3 | Mọi mốc thời gian trong DB là **UTC** (cột `timestamp` không múi giờ); API dùng ISO-8601 có hậu tố `Z`; UI chuyển sang GMT+7 | Spec (acceptance "timezone contract") |
| D4 | `rentals.vehicle_id` là **xe hiện hành** của đơn (đổi xe sẽ cập nhật, lịch sử nằm ở `vehicle_assignments`) — chi tiết ở FR-014 | Thiết kế |
| D5 | Xe `status = 'renting'` vẫn tìm được cho khung giờ **tương lai** (quyết định bằng booking, không bằng cờ `is_renting`) | Thiết kế |

## 3. Hợp đồng API

Controller mới `VehicleSearchController` (không nhét thêm vào `OperationsController`). `GET /api/vehicles` hiện có giữ nguyên cho quản trị (Staff+), không đổi hợp đồng.

| Method | Path | Auth | Mô tả |
|---|---|---|---|
| GET | `/api/vehicles/search` | **Anonymous** | Tìm xe khả dụng (công khai) |
| GET | `/api/vehicles/{id}/availability?from&to` | Anonymous | Xe có trống trong khung giờ không; trả lý do nếu không |
| GET | `/api/vehicles/{id}/busy-slots?from&to` | Anonymous | Các khoảng đã bận (đã gồm đệm) để vẽ lịch; không lộ thông tin khách |
| GET | `/api/vehicle-types/{id}/active-policy` | Anonymous | Chính sách thuê đang áp dụng (VAT, cọc, phí trễ) của loại xe |
| GET | `/api/vehicles/{id}/affected-rentals` | Admin, Manager, Staff | Các đơn tương lai bị ảnh hưởng khi xe vào bảo dưỡng (phục vụ đổi xe, FR-014) |

### 3.1 `GET /api/vehicles/search`

Query (tất cả tùy chọn trừ khi ghi chú):

| Tham số | Kiểu | Ghi chú |
|---|---|---|
| `from`, `to` | ISO-8601 UTC | Có cả hai hoặc không có cả hai. Không có: duyệt danh mục, **không** lọc theo lịch |
| `rentalType` | `hourly` \| `longterm` | Mặc định `hourly`; quyết định cột giá dùng để lọc/sắp xếp |
| `vehicleTypeId`, `facilityId` | int | |
| `q` | string (<= 60) | Tìm `brand`/`model` (không phân biệt hoa thường, bỏ dấu `%` `_`) |
| `brand` | string | Khớp chính xác (không phân biệt hoa thường) |
| `minPrice`, `maxPrice` | decimal >= 0 | Theo `rentalType` |
| `seatCount` | int | `>=` (chỉ áp cho ô tô thường) |
| `transmission` | string | `car_detail.transmission` |
| `bikeType` | string | `motorbike_detail.bike_type` |
| `minRangeKm` | decimal | Xe điện: `max_range_km >=` |
| `yearFrom` | int | `year_of_manufacture >=` |
| `fuelType` | string | `vehicle_types.fuel_type` |
| `hasDriver` | bool | `true`: chỉ trả khi còn ít nhất một tài xế khả dụng trong khung giờ; mỗi kết quả có `driverAvailable` |
| `sort` | `recommended`(mặc định) \| `price_asc` \| `price_desc` \| `newest` | `nearest` **dành riêng** cho BE-36, trả 400 `SORT_NOT_SUPPORTED` cho tới khi có |
| `page`, `pageSize` | int | `page >= 1`, `1 <= pageSize <= 100` (mặc định 12) |

Quy tắc xác thực `from`/`to`: `from < to`; `to - from <= 366 ngày`; `from >= now - 5 phút`; hourly tối thiểu 1 giờ (cấu hình `Availability:MinHourlyMinutes=60`). Vi phạm → 400 `INVALID_TIME_RANGE`.

Response `200` (qua `ApiSuccessResponse`), `data`:

```json
{
  "items": [{
    "id": 12, "vehicleType": { "id": 1, "name": "Ô tô", "fuelType": "Xăng" },
    "brand": "Toyota", "model": "Corolla Cross", "yearOfManufacture": 2024, "color": "Trắng",
    "mainImageUrl": "https://res.cloudinary.com/...", "location": "Bãi xe 1",
    "facility": { "id": 1, "name": "Chi nhánh Quận 1" },
    "priceHourly": 90000.0, "priceLongterm": 12000000.0,
    "summary": { "seatCount": 5, "transmission": "Tự động", "bikeType": null, "hasStorageBox": null, "maxRangeKm": null, "batteryCapacity": null },
    "badges": { "electric": false, "driverAvailable": true, "subscriptionEligible": true },
    "durationHours": 30.0
  }],
  "page": 1, "pageSize": 12, "totalCount": 120,
  "appliedWindow": { "from": "2026-10-10T02:00:00Z", "to": "2026-10-11T08:00:00Z", "bufferMinutes": 60 }
}
```

* Không trả: `created_by`, `status` nội bộ, thông tin chủ đơn khác. Chỉ trả xe đang đặt được (`available`/`renting`).
* **Không** tính báo giá (VAT/cọc) ở đây: công thức thuộc FR-017 do BE-12. FE-04 gọi endpoint báo giá của BE-12 khi có; trong lúc chờ dùng mock có công thức ở DESIGN.md §23.
* Không có `licensePlate` vì DDL chưa có cột này (câu hỏi mở O3).
* Header: `Cache-Control: no-store` khi có `from`/`to`; `public, max-age=30` khi chỉ duyệt danh mục.
* Rate limit: đặt dưới policy chung của BE-29; nếu chưa có thì thêm policy `public-search` 60 req/phút/IP bằng `AddRateLimiter`.

### 3.2 `availability` và `busy-slots`

`availability` → `{ "available": false, "reason": "BOOKED" | "MAINTENANCE" | "INACTIVE" | "NOT_FOUND", "conflictsWith": { "from": "...", "to": "..." } }` (khoảng xung đột đã gồm đệm, không kèm thông tin khách).
`busy-slots` → mảng `{ from, to, kind: "booking" | "maintenance" }`, đã cắt theo `[from,to]` yêu cầu (mặc định 31 ngày tới, tối đa 92 ngày).

### 3.3 `affected-rentals`

Trả các rental `pending`/`pre_inspection`/`renting` của xe có khoảng thuê giao với khoảng chặn của bảo dưỡng đang mở (`v_vehicle_maintenance_blocks`) hoặc bắt đầu sau `now`. Mỗi dòng: `rentalId, customerName, start, end, status`. Dùng cho staff khi xe vừa chuyển bảo dưỡng.

## 4. Quy tắc nghiệp vụ

**Một xe khả dụng cho `[from, to)` khi đồng thời:**

1. `vehicles.status IN ('available','renting')` (loại `maintenance`, `inactive`, NULL).
2. Không có `vehicle_bookings` với `is_blocking` mà `period && tsrange(from, to + 60 phút)`.
3. Không có khoảng chặn bảo dưỡng giao với `[from, to)` theo `v_vehicle_maintenance_blocks` (xem 5.2).
4. Nếu `hasDriver = true`: tồn tại ít nhất một tài xế khả dụng (staff `is_driver`, user `is_active`, bằng lái còn hạn đến hết `to`, không bị `vehicle_assignments` đang `assigned` trùng giờ — cùng nguồn với `IDriverAvailability` của FR-014; BE-33 sẽ bổ sung ca làm/ngày nghỉ qua cổng này).

**Rental chặn xe** khi `status IN ('pending','pre_inspection','renting','post_inspection')`; `completed`, `cancelled` không chặn.

**Bảo dưỡng chặn xe** khi `status IN ('open','scheduled','in_progress')` hoặc (`status = 'completed'` và `is_safe` không phải `true`). Khoảng chặn: từ `start_time` (hoặc 00:00 GMT+7 của `scheduled_date`) đến `end_time`; `open`/`in_progress` không có `end_time` và `completed` chưa an toàn: chặn **không thời hạn**; `scheduled` chỉ có ngày: chặn trọn ngày đó theo GMT+7. Giả định này ghi vào `docs/open-questions.md`.

## 5. Dữ liệu & migration `0008`

Chạy trong một transaction; **pre-check bằng khối `DO`** và dừng với thông báo rõ nếu dữ liệu vi phạm. File phải dùng **LF** (đã có sự cố checksum đa nền tảng, xem `changes/2026-10-02-*checksum*`). SQL dưới đây là **bản tham chiếu**: phải chạy thử trên PostgreSQL 16 (CI dùng `postgres:16-alpine`), chỉnh nếu lỗi cú pháp/ngữ nghĩa, và có integration test cho từng đối tượng.

### 5.1 Từ vựng `vehicles.status` + chỉ mục

```sql
CREATE EXTENSION IF NOT EXISTS btree_gist;
CREATE EXTENSION IF NOT EXISTS pg_trgm;

DO $$
DECLARE bad INTEGER;
BEGIN
  SELECT count(*) INTO bad FROM vehicles
   WHERE status IS NULL OR status NOT IN ('available','renting','maintenance','inactive');
  IF bad > 0 THEN
    RAISE EXCEPTION 'vehicles.status có % dòng ngoài tập hợp lệ; sửa dữ liệu trước khi chạy 0008', bad;
  END IF;
END $$;
ALTER TABLE vehicles ALTER COLUMN status SET NOT NULL;
ALTER TABLE vehicles ADD CONSTRAINT ck_vehicles_status
  CHECK (status IN ('available','renting','maintenance','inactive'));

CREATE INDEX IF NOT EXISTS ix_vehicles_search ON vehicles (vehicle_type_id, facility_id)
  WHERE status IN ('available','renting');
CREATE INDEX IF NOT EXISTS ix_vehicles_name_trgm ON vehicles
  USING gin ((lower(coalesce(brand,'') || ' ' || coalesce(model,''))) gin_trgm_ops);
CREATE INDEX IF NOT EXISTS ix_vehicle_maintenance_vehicle ON vehicle_maintenance (vehicle_id);
```

Ảnh hưởng chéo: `VehicleRequest.Status` của BE-09 (Kiệt) phải được validate theo bốn giá trị trên ở HTTP boundary để trả 400 thay vì 500 do CHECK. Báo Kiệt trước khi merge.

### 5.2 `vehicle_bookings` + trigger + view

```sql
CREATE TABLE IF NOT EXISTS vehicle_bookings (
  rental_id       INTEGER PRIMARY KEY,                     -- -> rentals.id (1 dòng / rental)
  vehicle_id      INTEGER NOT NULL,                        -- -> vehicles.id (xe hiện hành)
  starts_at       TIMESTAMP NOT NULL,                      -- UTC
  ends_at         TIMESTAMP NOT NULL,                      -- UTC
  buffer_minutes  INTEGER   NOT NULL DEFAULT 60,
  is_blocking     BOOLEAN   NOT NULL DEFAULT TRUE,
  period          tsrange GENERATED ALWAYS AS
                    (tsrange(starts_at, ends_at + make_interval(mins => buffer_minutes), '[)')) STORED,
  created_at      TIMESTAMP NOT NULL DEFAULT now(),
  CONSTRAINT ck_vb_range  CHECK (ends_at > starts_at),
  CONSTRAINT ck_vb_buffer CHECK (buffer_minutes >= 0),
  CONSTRAINT ex_vehicle_bookings_no_overlap
    EXCLUDE USING gist (vehicle_id WITH =, period WITH &&) WHERE (is_blocking)
);

CREATE OR REPLACE FUNCTION fn_rental_is_blocking(p_status TEXT) RETURNS BOOLEAN
LANGUAGE sql IMMUTABLE AS $$ SELECT p_status IN ('pending','pre_inspection','renting','post_inspection') $$;

CREATE OR REPLACE FUNCTION fn_sync_vehicle_booking_from_detail() RETURNS trigger
LANGUAGE plpgsql AS $$
DECLARE r RECORD;
BEGIN
  SELECT id, vehicle_id, status INTO r FROM rentals WHERE id = NEW.rental_id;
  IF NOT FOUND THEN RAISE EXCEPTION 'Rental % không tồn tại', NEW.rental_id; END IF;
  IF NEW.start_time IS NULL OR NEW.end_time IS NULL OR NEW.end_time <= NEW.start_time THEN
    RETURN NEW;  -- thiếu mốc giờ: ứng dụng phải validate trước, không tạo booking
  END IF;
  INSERT INTO vehicle_bookings (rental_id, vehicle_id, starts_at, ends_at, is_blocking)
  VALUES (r.id, r.vehicle_id, NEW.start_time, NEW.end_time, fn_rental_is_blocking(r.status))
  ON CONFLICT (rental_id) DO UPDATE
    SET vehicle_id = EXCLUDED.vehicle_id, starts_at = EXCLUDED.starts_at,
        ends_at = EXCLUDED.ends_at, is_blocking = EXCLUDED.is_blocking;
  RETURN NEW;
END $$;

CREATE TRIGGER trg_hour_detail_sync_booking
  AFTER INSERT OR UPDATE OF start_time, end_time ON hour_rental_detail
  FOR EACH ROW EXECUTE FUNCTION fn_sync_vehicle_booking_from_detail();
CREATE TRIGGER trg_longterm_sync_booking
  AFTER INSERT OR UPDATE OF start_time, end_time ON longterm_rental_contract
  FOR EACH ROW EXECUTE FUNCTION fn_sync_vehicle_booking_from_detail();

CREATE OR REPLACE FUNCTION fn_sync_vehicle_booking_from_rental() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
  IF NEW.vehicle_id IS DISTINCT FROM OLD.vehicle_id THEN
    -- đổi xe giữa chừng: chỉ giữ chỗ phần còn lại, tránh xung đột oan với lịch quá khứ của xe mới
    UPDATE vehicle_bookings
       SET vehicle_id  = NEW.vehicle_id,
           starts_at   = LEAST(ends_at - interval '1 minute', GREATEST(starts_at, (now() AT TIME ZONE 'utc'))),
           is_blocking = fn_rental_is_blocking(NEW.status)
     WHERE rental_id = NEW.id;
  ELSIF NEW.status IS DISTINCT FROM OLD.status THEN
    UPDATE vehicle_bookings SET is_blocking = fn_rental_is_blocking(NEW.status) WHERE rental_id = NEW.id;
  END IF;
  RETURN NEW;
END $$;
CREATE TRIGGER trg_rentals_sync_booking
  AFTER UPDATE OF vehicle_id, status ON rentals
  FOR EACH ROW EXECUTE FUNCTION fn_sync_vehicle_booking_from_rental();

CREATE OR REPLACE VIEW v_vehicle_maintenance_blocks AS
SELECT m.id AS maintenance_id, m.vehicle_id,
       tsrange(
         COALESCE(m.start_time, m.scheduled_date::timestamp - interval '7 hours', '-infinity'::timestamp),
         CASE
           WHEN m.status = 'completed' THEN 'infinity'::timestamp          -- chưa is_safe: chặn tiếp
           ELSE COALESCE(m.end_time,
                  CASE WHEN m.status = 'scheduled' AND m.start_time IS NULL AND m.scheduled_date IS NOT NULL
                       THEN m.scheduled_date::timestamp - interval '7 hours' + interval '1 day' END,
                  'infinity'::timestamp)
         END, '[)') AS blocked_range
  FROM vehicle_maintenance m
 WHERE m.status IN ('open','scheduled','in_progress')
    OR (m.status = 'completed' AND COALESCE(m.is_safe, FALSE) = FALSE);
```

Lưu ý triển khai:

* Nếu PostgreSQL từ chối exclusion trên cột generated, thay bằng biểu thức tương đương `tsrange(starts_at, ends_at + make_interval(mins => buffer_minutes), '[)')` trong `EXCLUDE` và trong truy vấn.
* Lỗi `SQLSTATE 23P01` (exclusion_violation) được ánh xạ thành `ConflictException("VEHICLE_UNAVAILABLE")` → 409.
* **Backfill:** chèn từ `rentals` JOIN `hour_rental_detail`/`longterm_rental_contract` theo `created_at`, `ON CONFLICT DO NOTHING`, rồi `RAISE NOTICE` số rental đang chặn nhưng không có booking (dữ liệu dev trùng lịch phải xử lý tay).
* Đệm lưu theo từng dòng (`buffer_minutes`); đổi giá trị mặc định đòi hỏi migration tính lại `period` — ghi trong ADR.
* Ứng dụng đọc `buffer_minutes` mặc định từ `Availability:BufferMinutes` (mặc định 60, `ValidateOnStart`) và **kiểm tra lúc khởi động** bằng bằng chứng `column_default` của bảng để phát hiện lệch cấu hình.

## 6. Kiến trúc & mã nguồn

| Tầng | Thành phần |
|---|---|
| Domain | `TimeRange` (value object: `from < to`, `Overlaps`, `WithBuffer`), `AvailabilityReason` (enum), `BookingStatusRules.IsBlocking(status)` — thuần, không phụ thuộc hạ tầng |
| Application | `IVehicleSearchRepository`, `IVehicleAvailabilityService` (`CheckAsync`, `AssertAvailableAsync(tx, vehicleId, window, excludeRentalId)`), `IDriverAvailability` (cổng cho FR-014/BE-33), `VehicleSearchQuery` (record + validate), `AvailabilityOptions` |
| Infrastructure | `NpgsqlVehicleSearchRepository`, `NpgsqlVehicleAvailabilityService`, `VehicleLock` (advisory lock helper) |
| WebApi | `VehicleSearchController`, DTO response, ánh xạ message trong `ApiSuccessResponseFilter` |

Khoá advisory: `pg_advisory_xact_lock(1001, @vehicleId)` (hai khóa int, khóa lớp 1001 = xe; FR-014 dùng 1002 cho tài xế). **Kiểm tra** các khóa đang dùng ở `AssignAsync` để không trùng không gian khóa. Khi cần khóa nhiều xe (đổi xe) phải khóa theo thứ tự `vehicle_id` tăng dần để tránh deadlock.

Quy trình trong transaction tạo rental (BE-12 tích hợp; BE-10 cung cấp hàm và vá `CreateRentalAsync` hiện tại cho đúng cơ chế): `BEGIN` → `VehicleLock.Acquire(vehicleId)` → `AssertAvailableAsync` → `INSERT rentals` → `INSERT detail` (trigger tạo booking, exclusion là lưới cuối) → `COMMIT`. Lỗi `23P01` ở bước INSERT nghĩa là có đường đi lách khóa: ghi log mức Warning kèm `vehicleId`.

Thời gian: truyền tham số `NpgsqlDbType.Timestamp` với `DateTimeKind.Unspecified` (Npgsql 8 **từ chối** `Kind=Utc` cho cột `timestamp`). Không đọc `DateTime.UtcNow` trực tiếp trong mã mới: dùng `TimeProvider` (đăng ký `TimeProvider.System`).

Lỗi: thêm `BusinessRuleException(code, message, status)` + `ConflictException`/`NotFoundException`/`ForbiddenException` trong `Application/Common/Errors`, mở rộng `UseExceptionHandler` để ghi `ProblemDetails.Extensions["code"]` (xem README mục "Quy ước chung"). Mã lỗi dùng ở spec này: `INVALID_TIME_RANGE`, `SORT_NOT_SUPPORTED`, `VEHICLE_UNAVAILABLE`, `VEHICLE_NOT_FOUND`.

## 7. Hiệu năng

* Mục tiêu: p95 < 300 ms với 5.000 xe và 200.000 booking (đo bằng `EXPLAIN (ANALYZE, BUFFERS)`, đính kèm vào PR; không đưa test thời gian vào CI để tránh flaky).
* Truy vấn chính dùng chỉ mục gist của exclusion (`period &&`) và `ix_vehicles_search`; đếm tổng bằng `COUNT(*) OVER()`; sắp xếp có tie-breaker `v.id`; `LIMIT/OFFSET` với `pageSize <= 100`.
* Không N+1: lấy chi tiết loại xe bằng LEFT JOIN các bảng `car_detail`, `motorbike_detail`, `electric_*_detail` (kiểm tra tên cột khoá thật trong `Init.sql`).

## 8. Kiểm thử (tối thiểu)

* **Domain (unit):** `TimeRange` (biên `[)`, đệm đối xứng), `IsBlocking` cho cả 6 trạng thái.
* **Integration (PostgreSQL thật):** (1) xe trống/bận theo đệm đúng 59/60/61 phút; (2) `renting` vẫn tìm được cho tương lai; (3) bảo dưỡng `open` không `end_time` chặn vô thời hạn, `scheduled` chặn đúng ngày GMT+7, `completed` + `is_safe=false` vẫn chặn; (4) mỗi bộ lọc và tổ hợp; (5) phân trang, sắp xếp ổn định; (6) `hasDriver`; (7) trigger tạo/cập nhật booking khi insert/đổi giờ/đổi trạng thái/đổi xe; (8) **đua tranh**: hai transaction song song tạo rental cùng xe cùng giờ — đúng một thành công, một nhận 409; (9) exclusion bắt được INSERT lách khóa (bỏ advisory lock trong test để chứng minh lưới cuối); (10) migration chạy được trên DB trống và trên DB có dữ liệu mẫu; chạy hai lần không lỗi.
* **API (WebApplicationFactory):** ẩn danh gọi được; 400 với tham số sai; không rò thông tin khách ở `busy-slots`; Swagger có đủ endpoint; mọi action có ánh xạ message.

## 9. Acceptance criteria

- [x] `GET /api/vehicles/search` công khai, đủ bộ lọc, phân trang, sắp xếp; không trả trường nội bộ
- [x] Đệm 60 phút đối xứng đúng ở tìm kiếm **và** ở tạo rental
- [x] Xe `maintenance`/`inactive`/có khoảng chặn bảo dưỡng không xuất hiện
- [x] Hợp đồng múi giờ: UTC trong DB và API; có test với giờ GMT+7 quy đổi
- [x] `vehicle_bookings` + trigger + exclusion hoạt động; backfill chạy được
- [x] Advisory lock + `AssertAvailableAsync` được dùng trong `CreateRentalAsync`; test đua tranh qua
- [x] `vehicles.status` có CHECK; BE-09 validate trả 400
- [x] `active-policy`, `availability`, `busy-slots`, `affected-rentals` có Swagger + ánh xạ message
- [x] Không có `Kind=Utc` ghi vào `timestamp` (test bao phủ)
- [x] Giả định ghi vào `docs/open-questions.md`; ADR "vehicle_bookings & đệm"; cập nhật `docs/progress.md`, `changes/YYYY-MM-DD-vehicle-availability.md`, `changes/CHANGELOG.md`, `docs/db` (Init.sql không đổi; cập nhật `docs/database.md`)

## 10. Câu hỏi mở / giả định cần xác nhận

* **O1.** Quy tắc chặn của bảo dưỡng (mục 4) là giả định; xác nhận với Kiệt (BE-24) các giá trị `status` thật của `vehicle_maintenance` (code hiện dùng `'open'`).
* **O2.** Cho phép đặt xe bắt đầu ngay trong đệm của lượt trước? Mặc định **không**.
* **O3.** `vehicles` chưa có biển số; FE mock đang hiển thị "biển số". Có thêm `license_plate` không?
* **O4.** Đổi giá trị đệm mặc định sau này cần migration tính lại `period` của booking đang chặn.

---

## Prompt thực thi (Claude Code — repo BE)

```text
Bạn là senior .NET/PostgreSQL engineer. Nhiệm vụ: hiện thực module BE-10 (FR-010) theo ĐÚNG file specs/FR-010-search-availability.md (đã cập nhật). Làm việc trong repo Car-Rental-System-BE, nhánh mới feat/be-10-availability từ main (git fetch trước; nếu main đã có migration 0008 thì đổi số).

ĐỌC TRƯỚC (bắt buộc, trước khi viết dòng code nào): agent.md; docs/architecture.md; docs/business-rules.md; docs/database.md; db/migrations/README.md; docs/decisions/ADR-001; specs/FR-010, FR-011, FR-014; src/VehicleRental.Application/Operations/OperationsContracts.cs; NpgsqlOperationsRepository (SearchVehiclesAsync, CreateRentalAsync, AssignAsync — xem cách dùng advisory lock hiện có); OperationsController; Program.cs (UseExceptionHandler, auth policies, rate limiter nếu có); ApiSuccessResponseFilter.cs; tests/ (PostgresFixture, Seed, Scenarios); changes/README.md và 2 file changes gần nhất.

QUY TẮC: tuân thủ nguyên văn agent.md (Clean Architecture một chiều, TreatWarningsAsErrors, không secret, ProblemDetails, mỗi API mới có Swagger metadata + ánh xạ ApiSuccessResponseFilter, ValidateOnStart cho Options). Mã mới đặt ở controller/service/repository MỚI, không nhét thêm vào OperationsController hay file repository dài hiện có; được phép sửa CreateRentalAsync tối thiểu để dùng VehicleLock + AssertAvailableAsync. Không dùng DateTime.UtcNow trong mã mới (TimeProvider). Không sửa file migration đã merge. File .sql dùng LF.

THỨ TỰ: (1) viết migration 0008 theo mục 5 + integration test cho schema/trigger/exclusion/view, chạy được trên DB trống và DB có dữ liệu; (2) Domain; (3) Application (ports, query, options, lỗi nghiệp vụ + mở rộng UseExceptionHandler với extension "code"); (4) Infrastructure; (5) WebApi (controller, DTO, Swagger, message mapping, rate limit); (6) vá CreateRentalAsync; (7) test theo mục 8; (8) tài liệu theo mục 9.

KIỂM CHỨNG và dán kết quả thật: dotnet build -warnaserror; dotnet test (nêu số test pass/fail); nếu Docker không có thì dùng TEST_DATABASE_ADMIN trỏ tới Postgres cục bộ và nói rõ; EXPLAIN (ANALYZE, BUFFERS) của truy vấn search trên dữ liệu seed lớn (>= 2.000 xe, >= 20.000 booking); gọi thử các endpoint trong Swagger.

BÁO CÁO CUỐI: danh sách file đổi, kết quả từng lệnh, các giả định đã ghi vào open-questions, rủi ro/điểm cần Lạc-Kiệt-Phúc xác nhận (CreateRentalAsync, CHECK vehicles.status, v.v.). Không tự ý mở rộng phạm vi. Nếu gặp mâu thuẫn giữa spec và code thực tế: dừng, mô tả, đề xuất phương án — không im lặng chọn một bên.
```
