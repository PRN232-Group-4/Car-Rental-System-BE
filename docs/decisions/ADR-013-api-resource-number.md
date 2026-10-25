# ADR-013 — Tên API theo số lượng tài nguyên

- Ngày: 2026-10-25
- Trạng thái: Chấp nhận theo yêu cầu người dùng

## Bối cảnh

Người dùng yêu cầu API dành cho nhiều tài nguyên có tên số nhiều, còn tài nguyên riêng lẻ có tên số ít. Các URL hiện tại dùng chung tên số nhiều cho cả danh sách và một ID.

## Quyết định

Tập hợp dùng số nhiều (`GET/POST /api/customers`). Tài nguyên được chọn bằng ID dùng số ít (`GET /api/customer/{id}`, `PUT/DELETE /api/vehicle/{id}`). POST tạo mới vẫn hướng vào tập hợp; số lượng bản ghi trong response không quyết định tên URL.

Tài nguyên con tuân theo cùng quy tắc: tập hợp vai trò ở `/api/user/{id}/roles`, gán một thành viên ở `/api/department/{departmentId}/member/{userId}`. Action/singleton như `approve`, `profile`, `contract`, `inspection-assignment` giữ tên đúng nghĩa. `staff` và `media` giữ nguyên; không tự thêm `s`. Nhóm chức năng không chọn một bản ghi bằng ID giữ tên theo chức năng. Lịch sử phân công là danh sách nên giữ `assignments`.

Thay trực tiếp URL trong BE, FE, test, schema và tài liệu đang dùng; không đăng ký alias URL cũ. Swagger là nguồn liệt kê contract mới. Giữ nguyên quyền, query/body, kiểu response và nghiệp vụ.

## Hệ quả

Đây là thay đổi URL không tương thích ngược. BE và FE cần được phát hành đồng bộ; các client bên ngoài cần chuyển sang URL mới. Header `Location` khi tạo tài nguyên cũng phải dùng URL mới. Không cần migration database.

Kiểm chứng bằng HTTP và Swagger, kiểm tra Location, regression hiện có và typecheck/test FE. Việc đổi tên API không đánh dấu hoàn thành các phần NFR-008 còn thiếu như versioning hoặc chuyển đổi pagination legacy.
