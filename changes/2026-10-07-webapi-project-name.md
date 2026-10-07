# VehicleRental.WebApi — 2026-10-07

Người dùng xác nhận folder HTTP phải là WebApi. Khôi phục folder/project/assembly
`VehicleRental.WebApi`, cập nhật namespace HTTP thành `Car_Rental_System_BE.WebApi`.
Solution, test project reference, Docker restore/publish/entrypoint và tài liệu trỏ tới WebApi.
Giữ nguyên Controllers/Middlewares, các layer khác, DI, route, response schema và nghiệp vụ.

Build warnings-as-errors và Release publish thành công (0 warning/error), tạo `VehicleRental.WebApi.dll`.
124/124 regression tests pass. Integration suite 183/183 đã pass trước thay đổi tên;
không chạy lại nhóm database vì lần này chỉ đổi tên project/namespace/path.
Docker build chưa kiểm chứng vì Docker Desktop engine chưa chạy.

API cũ đã xóa hết source và build artifacts. Root folder rỗng `src/VehicleRental.API`
còn bị Windows process khóa; không có file tracked hoặc project reference trong đó.
