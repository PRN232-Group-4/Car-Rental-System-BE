# ADR-014 — EF Core cho mọi truy vấn ghi và foreign key ở database

- Ngày: 2026-11-13
- Trạng thái: Chấp nhận. Thay phần "SQL vẫn dùng cho INSERT/UPDATE/DELETE" của [ADR-012](ADR-012-entity-framework-read-queries.md) và câu "schema không có FK" của [ADR-001](ADR-001-clean-architecture-repository.md).

## Bối cảnh

Sau ADR-012, đọc dữ liệu đã dùng EF nhưng còn khoảng 194 lệnh `NpgsqlCommand` viết tay cho ghi dữ liệu trong khoảng 30 file. Người dùng yêu cầu bỏ raw query và chuyển sang ORM, đồng thời thêm foreign key cho các bảng.

## Quyết định

**Ghi dữ liệu bằng EF Core.** Repository dùng `VehicleRentalDbContext` trên chính connection/transaction đang mở:
- INSERT: `db.InsertAsync(row)` (Add + SaveChanges). Khóa sinh bởi DB và các cột default `now()` được EF đọc lại qua `RETURNING`.
- UPDATE/DELETE: `ExecuteUpdateAsync` / `ExecuteDeleteAsync`, chạy một câu lệnh set-based và trả về số dòng bị ảnh hưởng như `ExecuteNonQuery`.
- `ON CONFLICT DO NOTHING`: `db.TryInsertAsync(row)`. Lệnh insert chạy trong savepoint; nếu gặp unique violation thì rollback về savepoint để transaction vẫn dùng được.
- `ON CONFLICT DO UPDATE`: nếu khóa dòng cha đã tuần tự hóa người ghi thì đọc rồi ghi; nếu không thì update, sau đó `TryInsert`, rồi update lại khi thua race.

**SQL còn lại có chủ đích**, chỉ nằm trong [DbLocks.cs](../../src/VehicleRental.Infrastructure/Persistence/DbLocks.cs):
- `pg_advisory_xact_lock`, `SELECT ... FOR UPDATE / FOR SHARE / FOR KEY SHARE / NOWAIT`, `LOCK TABLE` (LINQ không biểu diễn được). Định danh bảng/cột lấy từ EF model, giá trị luôn được tham số hóa.
- Đồng hồ DB `now()::timestamp`, `now() AT TIME ZONE 'utc'`, `CURRENT_DATE`. Giá trị lấy từ DB trong cùng transaction nên giữ nguyên ngữ nghĩa thời gian của SQL cũ.
- Migration runner (`DatabaseMigrationHostedService`) vẫn chạy DDL/bootstrap và file `.sql`. Đọc/ghi lịch sử và kiểm tra schema qua catalog dùng EF Core với `MigrationDbContext` riêng, không phụ thuộc bảng nghiệp vụ. Schema vẫn do SQL migration quản lý; không dùng EF Migrations.

Thứ tự khóa và transaction boundary giữ nguyên. Khóa `FOR UPDATE OF r` trên câu JOIN được tách thành: khóa dòng rentals, rồi đọc bằng LINQ trong cùng transaction.

**Mapping ghi:**
- Default hằng số (`status`, cờ `TRUE`, `buffer_minutes = 60`, mảng rỗng) là CLR initializer trên row.
- Default thời gian (`now()`) là `HasDefaultValueSql`, nên được bỏ qua khi giá trị CLR là `default(DateTime)`.
- Cột STORED generated `period` không bao giờ được ghi.
- Khóa tự nhiên (`vehicle_id`, `rental_id`, id thiết bị) là `ValueGenerated.Never`.

**Foreign key:** migration `0034_foreign_keys.sql` thêm FK cho mọi cột `*_id` logic đã ghi chú trong Init.sql/migration:
- FK được tạo `NOT VALID` rồi thử `VALIDATE`. Nếu dữ liệu cũ có bản ghi mồ côi, migration chỉ cảnh báo, không chặn khởi động; FK vẫn ràng buộc mọi dữ liệu ghi mới.
- `ON DELETE CASCADE` chỉ dùng cho bảng con thuần: `user_roles`, ảnh và chi tiết xe, telemetry của xe/thiết bị.
- `SET NULL` cho `vehicles.created_by`, `rental_policies.managed_by`, `vehicle_maintenance.inspection_id`.
- Các quan hệ còn lại dùng `NO ACTION`.
- EF model khai báo cùng các FK đó, không có navigation, với `DeleteBehavior.NoAction`.
- Xóa loại xe, gói, phòng ban hoặc cơ sở đang được tham chiếu trả 409 thay vì lỗi constraint.

## Hệ quả

- Ngoài `DbLocks` và migration runner, Repository không còn chuỗi SQL.
- Dữ liệu seed hoặc test phải tạo bản ghi cha trước. Bản ghi mồ côi cũ cần dọn rồi chạy `VALIDATE CONSTRAINT` thủ công.
- Tham số `timestamp` luôn gửi dạng UTC wall-clock `Kind=Unspecified` (`Sql.Ts`). Cột `timestamptz` (tracking) gửi `Kind=Utc`.
