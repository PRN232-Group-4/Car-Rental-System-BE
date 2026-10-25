# NFR-008 — OpenAPI

## Tên tài nguyên số ít/số nhiều — 2026-10-25

- Đường dẫn nhắm tới tập hợp dùng số nhiều: `/api/customers`, `/api/vehicles`, `/api/rentals`; POST tạo mới trên tập hợp giữ số nhiều.
- Tài nguyên có ID dùng số ít: `/api/customer/{id}`, `/api/vehicle/{id}`, `/api/rental/{id}`. Tài nguyên con áp dụng cùng quy tắc: `/api/user/{id}/roles` là tập hợp vai trò, `/api/department/{departmentId}/member/{userId}` nhắm một thành viên.
- Singleton/action và danh từ tập hợp hoặc không đếm được (`account`, `profile`, `staff`, `media`) không thêm `s` máy móc. Nhóm chức năng không đại diện một bản ghi có ID giữ tên theo chức năng.
- Cập nhật đồng bộ route, Location khi tạo, Swagger, tài liệu API, test và FE. Không đổi DTO, response envelope, phân quyền hoặc nghiệp vụ vì đổi URL.
- Acceptance: collection và singleton có đường dẫn đúng; URL detail cũ không xuất hiện trong Swagger; HTTP gọi route mới và Location hoạt động; danh sách con vẫn phân biệt với một bản ghi con.

Quyết định: [ADR-013](../docs/decisions/ADR-013-api-resource-number.md). Thay đổi: [gộp file và tên API](../changes/2026-10-25-file-consolidation-api-number.md).

## 2026-10-22 — Thứ tự mặc định danh sách Admin

- Xe: `created_at DESC, id DESC`; customer/GPLX, cơ sở, vai trò, hồ sơ nhân viên: ID giảm dần (các bảng này không có ngày tạo hồ sơ). SQL xếp trước LIMIT/OFFSET, giữ filter và tổng.
- FE mặc định nhân viên theo ID giảm dần thay cho ngày vào làm. Danh mục legacy trả đủ array (phòng ban, loại xe, gói) được xếp ID giảm dần trước chia trang ở FE; giữ contract API hiện có.
- Lịch chuyến/ca vẫn theo giờ bắt đầu; dashboard vẫn theo chỉ số. Các lựa chọn sort chủ động của người dùng được giữ.
- Acceptance: trang đầu/trang sau đúng thứ tự toàn cục, ổn định khi trùng ngày tạo; không xếp riêng từng trang.
- **Status:** In progress
Swagger non-production exists; log startup in ra URL Swagger; AGENT bắt buộc API mới có metadata OpenAPI. Remaining: versioning, API examples and auth schema.

## Tài liệu Markdown — 2026-10-07

- [x] Có [API reference tiếng Việt](../docs/API.md) đối chiếu controller/DTO/policy/response filter/repository.
- [x] Liệt kê đủ 90 controller endpoints; bổ sung root, health và callback Google middleware.
- [x] Mô tả cookie authentication, quyền và scope dữ liệu, request/response, mã HTTP thực tế và ví dụ frontend.
- [ ] Xác nhận tài liệu bằng HTTP smoke test trên hệ thống đang chạy.

Đây là bổ sung tài liệu, không đổi API hoặc đánh dấu hoàn thành versioning/auth schema/examples trong Swagger. Xem [change](../changes/2026-10-07-api-documentation.md).

## Response contract — 2026-10-07

Theo yêu cầu người dùng, response JSON có đủ sáu trường `code`, `status`, `is_success`, `data`, `message`, `error`. `code` là mã HTTP dạng số; `status` là `success`/`error`; `is_success` luôn là JSON boolean. Thành công có `error: null`; lỗi có `data: null` và giữ ProblemDetails/validation details trong `error`. Redirect OAuth/HTTPS và nội dung Swagger vẫn giữ giao thức riêng.

- [x] Success/empty success có đủ sáu trường, code khớp HTTP thực tế.
- [x] Lỗi controller, validation tự động, exception, 401/403 và 404/405 có cùng envelope.
- [x] Swagger mô tả đủ sáu trường cho success/error và kiểu boolean.
- [x] Test HTTP/serialization xác nhận contract, không bỏ trường null.

Quyết định: [ADR-002](../docs/decisions/ADR-002-api-response-envelope.md). Thay đổi: [response contract](../changes/2026-10-07-api-response-contract.md).

## Password validation schema — 2026-10-07

Swagger đọc StringLength của Password/NewPassword ở constructor parameter của record qua schema filter: minLength=6, maxLength=20. HTTP/Swagger tests xác nhận DTO đăng ký/đăng nhập và unit tests xác nhận giới hạn đồng nhất khi tạo nhân viên/đổi/reset mật khẩu. Xem [change](../changes/2026-10-07-password-length.md).

## Pagination contract — 2026-10-10

Mọi API trả về danh sách phải có pagination: query `page` mặc định **1**, `pageSize` mặc định **20**; `page >= 1` và `1 <= pageSize <= 100`. Response nằm trong `data` của envelope chung, gồm đủ `items`, `page`, `pageSize`, `totalCount`; `totalCount` là tổng số bản ghi sau lọc trước phân trang. Trang vượt phạm vi trả `items: []` và giữ metadata chính xác.

Validation bắt buộc tại cả HTTP boundary và Application service. Giá trị không hợp lệ trả 400 theo envelope lỗi chung; không tự clamp để bỏ qua validation. Query dùng thứ tự ổn định và tính offset tránh tràn số nguyên.

- [x] Ghi rule bắt buộc trong AGENTS.md và tài liệu API.
- [x] Customer list hiện đáp ứng contract và có test HTTP/Application/PostgreSQL.
- [ ] Chuyển toàn bộ API list cũ còn trả array sang contract pagination.

Khi thêm/sửa API list, Swagger phải mô tả default/range của query và schema page trong `data`. Test phải kiểm tra mặc định, các mốc hợp lệ 1/100, giá trị sai (page < 1, pageSize < 1 hoặc > 100), tổng sau lọc và trang rỗng. Quyết định: [ADR-003](../docs/decisions/ADR-003-list-pagination.md); thay đổi: [pagination rule](../changes/2026-10-10-list-pagination-rule.md).
