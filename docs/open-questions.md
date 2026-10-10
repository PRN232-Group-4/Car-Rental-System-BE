# Câu hỏi mở và giả định

## Customer directory và upload — 2026-10-10 assumptions

Danh bạ customer dùng cùng policy CustomerService với duyệt giấy phép (Admin/Manager/CustomerService), bao gồm hồ sơ inactive để phục vụ vận hành; caller lọc `isActive` khi cần. Detail dùng customer ID và chỉ trả thông tin liên hệ/license/loyalty đang có trong schema.

PUT license chính chuyển sang multipart để chọn file trong Swagger; JSON lưu URL chuyển tới `/api/customer/license/url`. Ảnh staff license/inspection/report dùng upload trước rồi gửi secureUrl vào JSON lưu nghiệp vụ. Hợp đồng ký lưu chứng từ bằng URL theo FR-013, chưa có yêu cầu upload PDF/tài liệu bằng Cloudinary trong thay đổi này.

## Reference layout — 2026-10-07 assumptions

Giữ prefix VehicleRental và suffix WebApi theo xác nhận của người dùng. Phân nhóm phụ được đặt dưới
Interfaces/DTOs/Services/Middlewares để các layer có đúng nhóm thư mục tham chiếu.
Domain port không sử dụng Application DTO; chỉ IUserRepository đủ điều kiện chuyển vào Domain.
ValueObjects chưa có model thực tế nên giữ bằng README. Properties/Program.cs/config vẫn là file
khởi chạy cần thiết của project API. Tổ chức file không thay đổi schema, route hoặc nghiệp vụ.

## Services/Repositories — 2026-10-07 assumptions

Yêu cầu “Services, Repository để query riêng” được triển khai thành Application Services,
persistence interfaces riêng và thư mục SQL repository riêng. Giữ nguyên API/database schema;
không tách project mới, không thêm generic repository. Các kiểm tra phụ thuộc dữ liệu dưới lock
được giữ trong transaction hiện tại; tách Operations theo aggregate là bước tiếp theo.

| Ngày | Liên quan | Giả định đã chọn | Cần xác nhận |
|---|---|---|---|
| 2026-10-03 | FR-007/008/026 | Department là tổ chức; staffRole cấp quyền. Một specialty cho mỗi staff trong API quản trị; Maintenance = Technician. Không tự cấp CustomerService cho Staff cũ. | Có cần kiêm nhiệm nhiều specialty qua API sau này? |
| 2026-10-03 | FR-007/026 | Maintenance làm việc toàn đội xe; facility và maintenance assignment chưa giới hạn truy cập. | Nếu phân tách cơ sở, bổ sung resource policy riêng. |
| 2026-09-30 | FR-001 | Hệ thống vận hành một đội xe, nên tạo `customers` cùng user đã verify. | Seed/role Customer có cần ngay khi RBAC làm? |
| 2026-09-30 | FR-001/003 | Redis URL dùng dạng StackExchange.Redis; `REDIS_URL` URI Railway có thể cần adapter. | Kiểm tra connection format Railway khi tạo project. |
| 2026-09-30 | FR-002 | Rate-limit edge chưa có vì chưa xác định reverse proxy. | Chọn ASP.NET rate limiter/Redis distributed limiter. |
| 2026-10-04 | O2/O4/O5/BE-24 | Inspection kiểm tra an toàn, không tự tính phí/hoàn cọc; staff report riêng. Ca trực độc lập bao phủ chuyến. Đổi xe giữ giá, đúng số chỗ; từ chối nếu thiếu seat_count. | Cụm "việc ko trả phí" hiểu là inspection không xử lý phí; công nợ/bồi thường riêng chưa nằm trong thay đổi này. |

## Department assignment — 2026-10-05 assumptions
Assignment moves an existing Staff/Manager account to a department and does not grant the Manager role. Manager assignment authority is system-wide, matching existing management policies. Multiple Managers may belong to one department; no single department-head relationship is introduced.

## Rental contracts — 2026-10-05 assumptions
Admin/Manager/CustomerService records externally signed documents by HTTP(S) URL and UTC signing time. Signed evidence is immutable and is recorded before rental start; corrections/versioning need a separate workflow. This API stores evidence and does not perform electronic signing. Existing rental start requirements are preserved; whether a signed contract must also gate long-term pickup remains a business decision.

## Response contract — 2026-10-07 assumptions

Chọn `code` là HTTP status dạng số và `status` là chuỗi `success`/`error` vì người dùng chưa quy định kiểu/giá trị của hai trường này. `is_success` luôn boolean theo yêu cầu explicit. Quyết định được ghi tại [ADR-002](decisions/ADR-002-api-response-envelope.md); có thể bổ sung mã lỗi nghiệp vụ riêng sau nếu cần.
