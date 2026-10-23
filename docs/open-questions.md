# Câu hỏi mở và giả định

## Mốc hết hạn của yêu cầu đặt xe — 2026-10-23, cần xác nhận

FR-042 được hiểu là **hạn duyệt + thanh toán tính từ thời điểm đặt**: `expire_at = created_at + ApprovalWindowMinutes` (mặc định 60 phút). Cách hiểu thay thế ("đơn hết hạn tại `start_time - 1 giờ`") chỉ cần đổi cách tính/backfill `rentals.expire_at` và `RentalExpiryOptions`, không cần đổi schema hay state machine. Đã chọn cách hiểu đầu vì đơn đặt xa ngày nhận vẫn phải được xử lý sớm để giải phóng lịch xe; nếu chủ sản phẩm chọn cách sau thì phải sửa cả thông báo UI và test. Backfill cho đơn đang mở trước khi triển khai dùng `GREATEST(created_at + 1 giờ, deploy + 1 giờ)` để không hết hạn hàng loạt đơn đang chờ; cần xác nhận có đồng ý cách xử lý này không.

Thông báo "sắp hết hạn/đã hết hạn" chưa triển khai: cần chốt có gửi email qua Brevo hay chỉ hiển thị trên giao diện trước khi bổ sung mẫu email.

## Hủy trước renting và backend đang chạy — 2026-10-22

Yêu cầu mới xác định hủy theo trạng thái thực tế, cho customer chủ đơn cả pending/pre_inspection kể cả sau giờ nhận dự kiến nếu chưa renting. Đây là thay đổi được người dùng yêu cầu, thay quyết định giới hạn thời gian cũ; áp dụng phí muộn hiện có thay vì tự miễn phí. Read-only Swagger Railway hiện chưa có POST checked dù route đã có ở source; cần triển khai backend mới, không thể sửa bằng tải lại FE. Không đổi proxy sang môi trường khác hoặc ghi trực tiếp dữ liệu Railway để né API thiếu.

## Xe customer và kiểm tra trước thuê — 2026-10-22

Hiểu “xe chưa được thuê” là xe available, không có đơn đang renting/post_inspection; đơn pending/pre_inspection vẫn được xét theo khoảng đặt và đệm. Xe đang thuê ẩn cả khi chọn lịch tương lai theo yêu cầu mới. Cả hourly/longterm cho chọn có tài xế/tự lái; xác nhận trực tiếp giới hạn Admin và phải chọn Maintenance active, còn Manager vẫn phân công chờ kỹ thuật. Dữ liệu mặc định có tài xế là xác nhận nghiệp vụ có nguồn system, không có số đo/ảnh giả. Các quyết định này thay luồng chauffeur-only trước đây; không thay giá/chính sách trễ/kiểm tra sau thuê.

## Timeout PostgreSQL metadata — 2026-10-22

Tái hiện Npgsql 8.0.5 lỗi tại `LoadBackendTypes`; tăng timeout lên 60 giây và thử driver 9.0.2 trong công cụ tạm vẫn thất bại. Kết nối với NoTypeLoading và truy vấn kiểu tích hợp hoạt động. Chưa xác định nguyên nhân khiến việc nạp metadata thất bại; dùng cờ `Database:SkipTypeLoading` theo môi trường như workaround, không đổi mặc định production. Schema ứng dụng hiện không khai báo enum/domain/composite tùy chỉnh cho truy vấn; cần đánh giá lại workaround nếu bổ sung các kiểu này. Driver của dự án vẫn là 8.0.5. Không lưu thêm connection string hoặc secret để bật cờ.

## OTP đổi mật khẩu — 2026-10-21

Giữ kiểm tra mật khẩu cũ và thêm OTP bắt buộc vào luồng đổi mật khẩu đã đăng nhập, dùng email từ claim và cấu hình RegistrationOptions hiện có. OTP ở namespace riêng với đăng ký/reset. Tài khoản chỉ đăng nhập Google, chưa có hash mật khẩu, vẫn dùng luồng quên mật khẩu để thiết lập mật khẩu trước; không thêm nhánh bỏ qua mật khẩu cũ. FE đếm cooldown mặc định 60 giây; backend là nguồn quyết định khi cấu hình thay đổi. Kiểm thử không gửi email thật. Xem FR-005 và change 2026-10-21-password-change-otp.

## Yêu cầu đặt xe và order — 2026-10-18, đã xác nhận

Người dùng xác nhận tách yêu cầu đặt xe và đơn thuê chính thức. Staff duyệt yêu cầu rồi mới cho thanh toán. PayOS xác nhận đã nhận tiền thì phát hành order paid; tiền mặt chờ staff xác nhận thực nhận rồi mới phát hành. Customer chọn phương thức trước khi gửi yêu cầu; dùng gói đã trả trước phát hành lúc duyệt. Rental ID giữ làm mã yêu cầu/vận hành hiện có; orderId riêng trả trong invoice/rental detail. Xem ADR-008 và FR-036; không còn giả định thanh toán trước confirm.

## Chọn cơ sở và tài xế khi đặt xe — 2026-10-18

Người dùng xác nhận “có tài xế” nghĩa là có tài xế rảnh tại cơ sở, không phải xe của đơn pending đã được assign. Tìm kiếm lọc staff facility, active, GPLX hợp lệ và assignment không chồng giờ có đệm. Giữ quy trình gán sau khi đặt; chưa giữ chỗ tài xế hoặc tự tạo assignment. Longterm tiếp tục tự lái vì API hiện không lưu hasDriver cho longterm. Email OTP Brevo dùng mẫu xác thực chung cho đăng ký/reset, không gửi email thật khi kiểm thử.

## Sửa rental trước thuê và bàn giao — 2026-10-16

“Trước khi thuê” được hiểu là pending/pre_inspection, chưa started_at; chỉ staff theo policy CustomerService sửa, khách vẫn xem đơn của mình. Chọn phạm vi PUT gồm vehicleId/startTime/endTime/hasDriver; danh tính khách, loại thuê, policy và gói không đổi. Chứng từ hoặc cam kết đã tồn tại chặn thay đổi thay vì tự hủy/ghi đè. Subscription chỉ dịch lịch cùng xe và thời lượng. Bàn giao dùng rentalId và state machine hiện có, không thêm trạng thái delivered. Link PayOS cũ không có bằng chứng phát hành, nên migration0020 khóa các invoice unpaid có sẵn khi nâng cấp lần đầu; không tự bỏ marker, kể cả gateway timeout/cancel. Workflow đối soát và mở lại sửa sau checkout nằm ngoài phạm vi này. Quyết định tại ADR-007.

## Kiểm tra loại xe và số lần thuê chính sách — 2026-10-15

Người dùng xác nhận chặn tạo mới khi loại xe có chính sách đang hoạt động; áp dụng cả scope chung null và khi kích hoạt bản inactive, không tự sửa dữ liệu active trùng đã có. Số lần thuê được hiểu là tổng đơn đã gắn policy, gồm mọi trạng thái (cả hủy), không chỉ đơn hoàn tất; FE ghi rõ cách đếm. Đơn policy null không được tự gán cho một phiên bản. Danh sách thay sang pagination theo ADR-003 vì bổ sung count vào API list.

## Trạng thái chính sách thuê xe — 2026-10-15

Theo yêu cầu bổ sung nút đặt lại status, cho phép Admin/Manager đổi riêng `isActive` của bản ghi được chọn. Không tự ngừng các phiên bản khác; thứ tự ưu tiên active mới nhất theo loại xe rồi policy chung vẫn giữ nguyên. Không sửa điều khoản hoặc snapshot rental đã tạo. Request đồng thời dùng giá trị ghi cuối; xem ADR-006. Ràng buộc chỉ một policy active cho mỗi phạm vi chưa thuộc yêu cầu này.

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

## Mapbox — 2026-10-20

Người dùng chọn thay toàn bộ Google Maps bằng Mapbox. Mặc định tìm địa chỉ tại Việt Nam và profile driving (không hứa giao thông live). Search Box POI chưa công bố hỗ trợ Việt Nam nên `/places` dùng address search; cần đánh giá coverage với token thật nếu cần POI. Backend geocode permanent do cảnh báo có thể lưu tọa độ; tài khoản phải đủ điều kiện. Chưa có token để xác nhận billing/coverage/domain restrictions hoặc deployment. Quyết định: [ADR-009](decisions/ADR-009-mapbox-provider.md).


## API completion — 2026-10-20 assumptions

Revenue tiếp tục theo paid invoice/issued_at như dashboard cũ; không đổi sang net cashflow. Snapshot trạng thái lấy thời điểm cuối kỳ hoặc hiện tại nếu kỳ tương lai, nêu rõ asOf. Bộ lọc cơ sở/loại xe dùng metadata xe hiện tại vì chưa có snapshot lịch sử. Subscription usage dùng giờ lịch thuê + overtime theo đơn; chưa bổ sung ledger hoàn/trừ giờ. Metadata gói trước đây dùng hạn mức gói hiện tại, không giả snapshot. Chưa nhập năm xe/hotline/địa chỉ/tiện nghi thiếu dữ liệu; giữ null/empty. Facility detail đổi sang object để đáp ứng yêu cầu; FE cần đồng bộ adapter. API danh sách được sửa trong đợt này chuẩn hóa pagination theo ADR-003.

## Audit logs và role catalog — 2026-10-21 assumptions

Audit logs sử dụng nhật ký quản trị tài khoản đã có, mở quyền đọc Management (Admin/Manager); các mutation tài khoản và CRUD role giữ Admin. Chuẩn hóa action lịch sử sang tên public theo yêu cầu, details dùng reason đã lưu và IP lịch sử giữ null. Tạo role là tạo danh mục dùng được cho membership; tên tùy chỉnh không tự tạo policy/capability mới. Bảo vệ role hệ thống và chặn đổi tên/xóa role tùy chỉnh đang gán để giữ tính nhất quán phiên và tham chiếu. Migration unique tên role không tự gộp bản ghi cũ.
