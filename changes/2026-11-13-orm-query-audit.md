# 2026-11-13 — Rà soát raw query còn lại trong BE

- **FR/NFR:** NFR-001, ADR-014
- **Loại:** refactor

## Đã làm

- Rà toàn bộ `src`: repository nghiệp vụ đã dùng EF Core; raw API còn trong `DbLocks` và migration runner.
- Chuyển truy vấn checksum, ghi lịch sử migration và kiểm tra schema từ `NpgsqlCommand` sang LINQ/SaveChanges. `MigrationDbContext` riêng ánh xạ lịch sử và catalog PostgreSQL, dùng được trước khi schema nghiệp vụ tồn tại; ghi history tham gia transaction của script.
- Thêm architecture test chặn raw API ngoài primitive PostgreSQL và runner schema; thêm regression baseline, chạy lại, checksum mismatch, rollback/retry migration.
- Sửa điều kiện null trong `SaveDriverShiftAsync`: khi không có tài xế cũ, không coi assignment tự lái (`driver_staff_id IS NULL`) là assignment của tài xế đang sửa. Thêm regression riêng.
- Cập nhật seed nhân viên/đơn thuê để đáp ứng FK; test xác minh FK từ chối bản ghi mồ côi và bảo vệ tài khoản có tham chiếu. Test lỗi ghi ảnh kiểm tra `DbUpdateException` cùng SQLSTATE gốc và rollback; status history kiểm tra đúng trigger đã tách ở migration 0033.
- Giữ SQL cho khóa advisory/dòng/bảng và đồng hồ transaction trong `DbLocks`, DDL/bootstrap và file migration. LINQ không cung cấp trực tiếp primitive khóa tương đương. SQL seed/assert trong test không phải persistence runtime.

## Kiểm chứng

- `dotnet build Car-Rental-System-BE.sln --no-restore -m:1 -warnaserror`: 0 warning/error.
- Toàn suite: **1.257 passed, 0 failed, 0 skipped**, 31 giây, PostgreSQL 18 tạm cổng 55439, timezone UTC; không dùng DB ứng dụng.
- Rà `src` và architecture test xác nhận raw API chỉ còn trong `Persistence/DbLocks.cs` và DDL/file script ở `HostedServices/DatabaseMigrationHostedService.cs`.
