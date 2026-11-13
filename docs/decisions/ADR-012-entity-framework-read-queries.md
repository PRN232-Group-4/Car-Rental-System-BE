# ADR-012 — Entity Framework Core cho truy vấn đọc

- Ngày: 2026-10-25
- Trạng thái: Chấp nhận; phần ghi dữ liệu bằng SQL được thay bởi [ADR-014](ADR-014-ef-writes-and-foreign-keys.md)

## Bối cảnh

Backend có nhiều câu SELECT và mapper theo ordinal viết tay. Người dùng yêu cầu dùng ORM Entity Framework và giảm các file nhỏ rời rạc. Schema PostgreSQL, trigger, exclusion constraint và thứ tự khóa đang bảo vệ đặt lịch, thanh toán và chuyển trạng thái thuê.

## Quyết định

Infrastructure dùng EF Core 8 với `Npgsql.EntityFrameworkCore.PostgreSQL` cho truy vấn đọc thông thường. `VehicleRentalDbContext` và các row ánh xạ nằm trong Persistence; Application/Domain không tham chiếu EF. DTO được project bằng LINQ; lọc, đếm tổng, sắp xếp và phân trang thực hiện ở database. Truy vấn dashboard aggregate tại PostgreSQL trước khi materialize.

Các row gom trong `PersistenceRows.cs`, ánh xạ tên bảng/cột và kiểu dữ liệu hiện có. Ba view `customer_license_records`, `v_vehicle_maintenance_blocks`, `v_loyalty_reconciliation` là keyless. Không có navigation tạo FK ngầm; không `EnsureCreated`, `Migrate` hoặc EF migration. SQL migration hiện có tiếp tục là nguồn quản lý schema.

Context mặc định no-tracking; repository dùng factory theo thao tác, không đăng ký thêm scoped DbContext không được dùng tại composition root. Mỗi thao tác tạo context ngắn hạn từ connection do `NpgsqlDataSource` cấp, giữ pool và cấu hình driver hiện có. Overload nhận connection/transaction đang chạy để LINQ đọc cùng giao dịch; dispose context không đóng connection do bên gọi sở hữu. Các truy vấn nhiều bước vốn dùng một snapshot tiếp tục có `RepeatableRead`.

SQL vẫn dùng cho INSERT/UPDATE/DELETE/RETURNING, PostgreSQL advisory/row lock, kiểm tra nghiệp vụ đang gắn với khóa và một số truy vấn nội bộ trong transaction ghi. Không đổi thứ tự khóa chỉ để bỏ SQL. Các lớp giữ tên `Npgsql*` để tương thích constructor/DI/test; PostgreSQL vẫn là provider.

Các khai báo nhỏ cùng feature có thể chung file: request/response DTO, exception riêng của port, partial interface nhỏ. Giữ riêng controller, service, repository và các phần Operations lớn. Không đổi API/namespace để giảm file.

## Kiểm chứng

- Model test kiểm tra no-tracking, không FK ngầm, kiểu timestamp và view.
- Integration test query tất cả row/view trên schema sau migration, đọc dữ liệu chưa commit trong transaction dùng chung và kiểm tra ownership connection.
- Regression PostgreSQL và HTTP hiện có kiểm tra truy vấn, phân trang, xác thực, booking và concurrency; test GPS bổ sung kiểm tra khoảng cách, null, lịch sử và trang trống.
- Baseline HEAD được build/test riêng để phân biệt lỗi có sẵn; kết quả cuối ghi tại change/progress.

Tham khảo: [EF Core — Transactions](https://learn.microsoft.com/en-us/ef/core/saving/transactions).
