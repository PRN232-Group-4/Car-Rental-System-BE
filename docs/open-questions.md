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
| 2026-10-14 | FR-029 | Pha 1 Loyalty chỉ hiện thực Tích điểm (Earn) & Điều chỉnh điểm (Adjust). Tiêu điểm (Redeem) chỉ có cổng Domain/Application, cấu hình Redemption.Enabled=false; gọi trả về 409 REDEMPTION_DISABLED để không ảnh hưởng luồng thanh toán hiện tại của BE-12/18. | Đã thống nhất theo spec FR-029, xem ADR-005. |
| 2026-10-14 | FR-029 | Căn cứ tích điểm (basisAmount) là net rent trước thuế (rent_price), loại trừ VAT và tiền cọc; đơn thuê theo gói (subscription) không tích điểm. | Đã triển khai và có test kiểm chứng. |
| 2026-10-14 | FR-029 | Số dư đệm `customers.loyalty_point` được đồng bộ tự động qua AFTER INSERT trigger trên `customer_loyalty_transactions`; tuyệt đối không update thủ công ngoài code để tránh cộng đôi. | Xem ADR-005. |
| 2026-10-06 | FR-018 | Gỡ bỏ auto-refund tại AddInspectionAsync: hoàn cọc chỉ xử lý duy nhất tại complete sau khi tính toán quá giờ để tránh hoàn cọc 2 lần hoặc thất thoát. | Đã thống nhất với Huy (O2), đã kiểm chứng qua test. |
| 2026-10-06 | FR-018 | Chính sách hủy đơn: khách hủy trước 24h hoàn 100%; dưới 24h giữ phí phạt 20% tiền thuê, hoàn 80% tiền thuê + 100% cọc; Staff/System hủy hoàn 100%. | Đã thống nhất (O1), xem ADR-004. |
| 2026-10-06 | FR-018 | Hóa đơn thiếu hụt quá giờ: tạo với kind = 'overtime', hạn thanh toán 72h; partial unique index chặn trùng lặp. | Xác nhận với Phúc (BE-18/19) cách xử lý thanh toán hóa đơn này (O3). |
| 2026-10-06 | FR-018 | Thuê bằng gói thuê bao khi trả trễ: trừ giờ trễ vào remaining_hours; không thu/hoàn cọc tiền mặt. | Đã triển khai và tích hợp với ISubscriptionUsage (O4). |
| 2026-10-06 | FR-018 | Xe hỏng khi nhận lại (is_ok = false): đánh dấu damage_review_required = true trên rental_settlements để Staff lập biên bản sự cố riêng sau. | Đã thống nhất với Huy (O4), xem ADR-004. |
| 2026-10-04 | FR-014 | Đổi xe khác loại: hiện tại chặn (409 VEHICLE_TYPE_MISMATCH) để tránh phá vỡ tính toán giá ban đầu. | Cần Lạc (BE-12/33) xác nhận nếu muốn cho phép đổi chéo loại xe kèm tính bù trừ chênh lệch (O2). |
| 2026-10-04 | FR-014 | Kiểm tra hạng GPLX theo loại xe theo luật mới: tạm tắt qua config (Assignment:CheckLicenseClass=false). | Chờ nhóm nghiệp vụ cung cấp ma trận phân hạng GPLX tương thích (O3). |
| 2026-10-04 | FR-014 | Kiểm định xe thay thế: bảng inspections hiện có UNIQUE(rental_id, inspection_type) nên xe thay thế chưa thể ghi thêm pre-inspection. | Đề xuất cho BE-15/16 (Lạc/Anh) bổ sung cột assignment_id vào bảng inspections (O4). |
| 2026-10-04 | FR-014 | Phụ thuộc vòng giữa BE-33 và BE-14: giải quyết bằng cổng IDriverAvailability mặc định trong Application. | Cần Lạc (BE-33) xác nhận triển khai adapter ca làm việc/ngày nghỉ qua cổng này (O5). |
| 2026-10-04 | FR-010 | Quy tắc chặn của bảo dưỡng: coi các trạng thái 'open', 'scheduled', 'in_progress', hoặc 'completed' mà chưa 'is_safe' là đang chặn xe. | Xác nhận với Kiệt (BE-24) các giá trị status thật của bảng vehicle_maintenance (hiện code dùng 'open'). |
| 2026-10-04 | FR-010 | Đệm 60 phút đối xứng ở cả 2 đầu: không cho phép đặt xe bắt đầu ngay trong thời gian đệm của lượt thuê trước. | Xác nhận với Phúc/Lạc xem khách có được nhận xe sớm trong đệm nếu xe đã vệ sinh xong không. |
| 2026-10-04 | FR-010 | Bảng vehicles hiện chưa có cột license_plate; FE mock đang hiển thị biển số. Giữ nguyên schema, không tự ý thêm cột. | Xác nhận với nhóm xem có cần migration thêm license_plate vào bảng vehicles không. |
| 2026-10-04 | FR-010 | Đệm mặc định lưu theo dòng (buffer_minutes = 60). Thay đổi đệm mặc định sau này đòi hỏi migration tính lại cột period. | Xem chi tiết trong ADR-002. |
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
