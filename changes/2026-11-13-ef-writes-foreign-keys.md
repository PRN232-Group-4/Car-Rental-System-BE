# 2026-11-13 — Ghi dữ liệu bằng EF Core và thêm foreign key
- **FR/NFR:** NFR (kiến trúc persistence), ADR-014
- **Loại:** chore
## Đã làm
- Chuyển mọi INSERT/UPDATE/DELETE/upsert viết tay trong Infrastructure sang EF Core (`InsertAsync`, `TryInsertAsync`, `ExecuteUpdateAsync`, `ExecuteDeleteAsync`). Các repository đã chuyển: Account, User, UserAdministration, Role, Staff, Operations (main/Editing/Vehicles/Licenses/Policies/Recovery), OperationsCompletion, InspectionAssignment, Payment (+Cash), Loyalty, Tracking, Assignment, RentalLifecycle, RentalContractDocument, RentalLifecycleAdapters, RentalOrderWriter, RentalApprovalWriter, VehicleLock, DriverLock.
- Thêm `Persistence/DbLocks.cs`. Đây là nơi duy nhất còn SQL, gồm advisory lock, khóa dòng/bảng, đồng hồ DB và insert-ignore bằng savepoint.
- Self-drive eligibility chuyển từ chuỗi SQL `CustomerLicenseSql` sang LINQ `CustomerLicenseEligibility`.
- `VehicleRentalDbContext`: default `now()`, cột generated `period`, khóa tự nhiên và FK (không navigation). Row có CLR initializer theo default của DB.
- Migration `db/migrations/0034_foreign_keys.sql` thêm FK (NOT VALID rồi VALIDATE) cho mọi quan hệ logic và index cho `vehicle_imgs.vehicle_id`, `user_roles.role_id`, `vehicle_positions.device_id`.
- Xóa loại xe đang dùng trong policy trả 409. Gói đã có payment được ngưng thay vì xóa. Xóa phòng ban hoặc cơ sở đang được tham chiếu trả 409.
## Chưa làm
- Đã bổ sung kiểm chứng trên PostgreSQL tạm trong [đợt rà soát ORM](2026-11-13-orm-query-audit.md): toàn suite 1.257 pass, 0 fail/skip.
- Migration runner vẫn chạy file SQL (giữ nguyên theo quyết định).
## Test
- `dotnet build` sạch. Unit test: 890 pass, giống baseline trước thay đổi.
- 364 test integration lỗi vì không kết nối được DB, giống baseline.
- Model test cập nhật: có FK, không navigation, `DeleteBehavior.NoAction`.
## Quyết định/giả định
- Xem ADR-014. Thứ tự khóa giữ nguyên; `FOR UPDATE OF r` trên JOIN được tách thành khóa dòng rentals rồi đọc LINQ.
- Giả định session PostgreSQL dùng UTC như mã hiện có. Tham số timestamp gửi UTC wall-clock `Unspecified`.
