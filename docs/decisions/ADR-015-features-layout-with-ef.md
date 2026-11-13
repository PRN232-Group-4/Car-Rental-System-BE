# ADR-015 — Features layout và EF Core

- Ngày: 2026-11-13
- Trạng thái: Chấp nhận theo lựa chọn của người dùng. Thay phần tổ chức thư mục trong ADR-001; giữ các ràng buộc persistence/FK của ADR-014.

## Quyết định

- Tích hợp `refactor/features-layout` vào `refactor/db`. Nhánh `refactor/src-project-layout` là ancestor cũ, không chứa cấu trúc feature mới mà người dùng muốn.
- Mỗi layer tổ chức nghiệp vụ ở `Features/<Module>`; không đổi chiều dependency Domain <- Application <- Infrastructure <- WebApi.
- Repository được tách theo feature; các thao tác đã chuyển ORM ở `refactor/db` được chuyển sang repository tương ứng. Các writer mới của nhánh Features cũng dùng EF Core và tham gia cùng transaction.
- Row mapping ở `Infrastructure/Persistence/Rows`, giữ default, generated column, timestamp và FK hiện có.
- Bộ test tách thành Domain/Application/Integration/Architecture; giữ regression raw-query và foreign-key từ nhánh DB.
- Giữ các sửa lỗi quyết toán một transaction, ledger giờ gói và hóa đơn supplementary của nhánh Features; ledger được ánh xạ EF cùng hai FK của nó.
- Giữ nguyên file/checksum migration đã tồn tại ở cả hai nhánh. Runner định danh migration bằng tên đầy đủ; các file có cùng tiền tố số nhưng khác hậu tố vẫn là hai migration độc lập, sắp xếp ổn định theo tên.

## Kiểm chứng bắt buộc

Build warnings-as-errors; toàn suite trên PostgreSQL tạm; rollback, idempotency, chống race và supplementary payment; catalog FK khớp model. Không migrate DB ứng dụng trong quá trình merge.
