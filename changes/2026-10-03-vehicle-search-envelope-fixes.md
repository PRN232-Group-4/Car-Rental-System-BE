# 2026-10-03 — Sửa `GET /api/vehicles` 500 và lỗi test hết hạn gói

- **FR/NFR:** FR-008..011 (catalog/operations), NFR-006..007 (testing)
- **Loại:** fix

Phát hiện khi verify luồng mua gói ngoài production: `GET /api/vehicles` luôn trả 500, chặn toàn bộ danh sách xe (booking + landing). Có **hai** nguyên nhân độc lập, sửa cả hai.

## Đã làm

### 1. `42P08: could not determine data type of parameter $1` (repository)

`NpgsqlOperationsRepository.SearchVehiclesAsync` dựng bộ lọc `($1 IS NULL OR v.vehicle_type_id=$1) AND ...`. Khi tham số là `DBNull` (Npgsql gửi type `unknown`), PostgreSQL không suy được kiểu của `$1` trong `$1 IS NULL` → `Npgsql.PostgresException 42P08` → 500.

- Thêm cast tường minh: `$1::int`, `$2::int`, `$3::text`, `$4::timestamp`, `$5::timestamp`.
- Test `VehicleSearchTests` tái hiện đúng lỗi cũ (gọi `SearchVehiclesAsync` với toàn tham số NULL).

### 2. `InvalidCastException` khi serialize envelope (WebApi)

`ApiSuccessResponseFilter` thay `ObjectResult.Value` bằng `ApiSuccessResponse` nhưng **giữ nguyên `ObjectResult.DeclaredType`** (kiểu trả về của action). Với action khai báo kiểu cụ thể (`Task<VehiclePage>`), `System.Text.Json` cố cast envelope về `VehiclePage` → `Unable to cast object of type 'ApiSuccessResponse' to type 'VehiclePage'` → 500. Các action trả `IReadOnlyList<T>` không dính nên lỗi chỉ lộ ở `/api/vehicles` (và các action trả một object như `Task<Rental>`, `Task<CustomerLicense>`).

- Sau khi wrap, đặt `objectResult.DeclaredType = null` để serializer dùng kiểu thực tế của envelope.
- Test hồi quy trong `UnitTests` (dựng `ResultExecutingContext` với `DeclaredType = typeof(VehiclePage)`, assert `DeclaredType` bị xoá và giá trị là `ApiSuccessResponse`).

### 3. Test có sẵn vi phạm `ck_cs_dates`

`SubscriptionExpiryTests.Expire_job_marks_past_end_date_and_runs_idempotently` đặt `end_date = hôm qua` trong khi `start_date = hôm nay` → vi phạm `CHECK (end_date >= start_date)` → test fail, kéo theo `Reminder_lists_only_active_rows_...` fail dây chuyền (dữ liệu của test trước còn `active` trong cửa sổ nhắc).

- Backdate cả khoảng: `start_date = today-3`, `end_date = today-1`.

## Test

- `VehicleSearchTests` (mới, 2 test): tìm không filter trả về xe; filter theo `status` + phân trang.
- `UnitTests`: +1 test hồi quy cho response filter.
- `SubscriptionExpiryTests`: 2 test chuyển từ fail sang pass.
- `dotnet build -warnaserror`: 0 warning / 0 error. `dotnet test`: **84/84 pass**.
- Smoke test HTTP local: `GET /api/vehicles` → `200 {"is_success":true,"data":{"items":[],"page":1,"pageSize":20,"totalCount":0}}`.

## Quyết định/giả định

- Hai lỗi này không thuộc nhóm task BE-18..BE-23 nhưng chặn FE nên sửa luôn.
- `ObjectResult.DeclaredType = null` áp dụng cho mọi action (không chỉ `Vehicles`); các endpoint collection vẫn hoạt động như trước.
