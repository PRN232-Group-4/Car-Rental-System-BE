# 2026-11-13 — Kiểm chứng foreign key cho toàn bộ schema

- **FR/NFR:** NFR-001, ADR-014
- **Loại:** test/docs

## Kết quả

- Schema sau toàn bộ migration có **46 bảng nghiệp vụ, 88 foreign key**; tất cả đã validate trên DB sạch và khớp bảng/cột đích trong EF model.
- Migration `0034_foreign_keys.sql` đã bổ sung **82 FK**; **6 FK** có từ migration 0013/0017. Không phát hiện quan hệ thiếu để cần thêm migration mới.
- **40 bảng** có FK đi ra. Sáu bảng gốc/danh mục (`users`, `roles`, `departments`, `facilities`, `vehicle_types`, `subscription_plans`) không có cột tham chiếu nên không cần FK đi ra.
- `customer_loyalty_transactions.ref_id` là tham chiếu đa hình theo `ref_type`, không thể gắn FK cố định vào một bảng. `schema_migrations` là lịch sử kỹ thuật độc lập; view không có FK riêng.

## Kiểm chứng

- Thêm `ForeignKeySchemaTests`: so catalog PostgreSQL với toàn bộ quan hệ ORM và các cột tham chiếu; kiểm tra validation, migration chạy lại không tạo trùng; mô phỏng FK thiếu trên dữ liệu legacy mồ côi, giữ nguyên dữ liệu cũ nhưng từ chối tham chiếu sai mới, validate thành công sau khi bổ sung bản ghi cha.
- Build warnings-as-errors: **0 warning/error**.
- Toàn bộ suite: **1.260 passed, 0 failed, 0 skipped**, 33 giây trên PostgreSQL 18 tạm UTC.
- Không kết nối/thay đổi DB ứng dụng hoặc production. Database đang chạy nhận FK khi triển khai/chạy migration 0034; dữ liệu cũ mồ côi để lại constraint `NOT VALID` cùng warning và cần xử lý rồi `VALIDATE CONSTRAINT` theo ADR-014.
