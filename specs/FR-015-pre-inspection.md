# FR-015 — Pre-rental inspection

## Thay đổi theo yêu cầu 2026-10-22

Đồng bộ tiến trình: POST checked nhận thêm `approveRequest:boolean=false`. Khi Admin chọn rõ xác nhận kiểm tra kèm duyệt yêu cầu, flag=true làm pending chuyển pre_inspection, ghi confirmed_at/history cùng transaction với evidence và phân công. Dùng cùng writer với duyệt lifecycle; gói/tiền đã nhận chỉ phát hành order theo quy tắc hiện có. Flag=false, phân công đơn thuần, kiểm tra kỹ thuật và driver_default không tự duyệt. Không tự bắt đầu thuê hoặc đánh dấu đã thanh toán. Đơn cũ pending đã có pass dùng endpoint confirm riêng, không ghi đè evidence.

Đơn có tài xế được mặc định đạt kiểm tra trước thuê khi tạo và khi đổi xe. Ghi nhận riêng `confirmation_source=driver_default`, `confirmed_by=system`, không tạo số đo hay ảnh giả. Đơn tự lái: Admin/Manager có thể chỉ phân công Maintenance để chờ kiểm tra, hoặc chọn kỹ thuật viên và xác nhận đã phân công & kiểm tra qua POST `/api/rentals/{id}/inspection-assignment/checked`. Cách thứ hai lưu phân công và kết quả đạt trong cùng transaction, nguồn `admin_confirmation`, người xác nhận lấy từ principal. Không ghi đè biên bản đã có (kể cả fail); không cho thao tác sau khi bắt đầu. Chỉ Admin được xác nhận trực tiếp; Manager vẫn có thể phân công. Các điều kiện thanh toán, tài xế thực tế, bảo dưỡng và kiểm tra sau thuê giữ nguyên.

## Danh sách evidence — 2026-10-21

GET `/api/inspections` đọc tất cả evidence trước/sau thuê, không giới hạn xe hiện tại hoặc rental đang chạy. Admin/Manager/CustomerService/Maintenance được đọc. Lọc tùy chọn inspectionType=pre_rental/post_rental và rentalId dương; phân trang mặc định 1/20, pageSize 1–100, totalCount sau lọc. Thứ tự inspected_at DESC NULLS LAST rồi id DESC. Evidence trả tên xe/biển số/email người kiểm tra đã ghi cùng các thông số, thời gian và ảnh. Danh sách việc chưa làm được khám phá từ rental queue hiện có, không tạo evidence giả cho việc chưa kiểm tra.

- [x] HTTP/DB regression bao phủ all history gồm xe cũ, lọc kết hợp/tổng sau lọc, trang vượt phạm vi và size 1/100.
- [x] HTTP/Application validation, quyền đọc, envelope 6 trường và Swagger được kiểm tra.
- **Status:** Implemented; **Priority:** P0; **Milestone:** M4

Một pre-inspection/rental/vehicle, evidence bất biến; is_ok=true của xe hiện tại mới được renting. Fail giữ pending, khóa xe để Manager thay xe cùng số chỗ; không tự hủy rental, tạo bảo trì hoặc tính phí.
Admin/Maintenance có staff profile thực hiện. staffId trong body phải trùng claim; luôn lưu người thực hiện từ principal. Xe thay thế có endpoint inspection riêng. Xem FR-031.

## Phân công kiểm tra trước thuê — 2026-10-21

Admin/Manager phân công một nhân viên Maintenance đang hoạt động cho rental/xe hiện tại ở pending hoặc pre_inspection, trước khi đã có evidence kiểm tra của xe đó. GET/PUT `/api/rentals/{id}/inspection-assignment` đọc/lưu trách nhiệm kiểm tra; PUT nhận `technicianStaffId`, người giao lấy từ principal. Admin/Manager/CustomerService/Maintenance được đọc phân công. Phân công không thay thế kết quả kiểm tra, không đổi trạng thái rental và không thay quyền ghi evidence hiện tại. Đổi kỹ thuật viên chỉ trước evidence; xe thay phải được phân công riêng.

Operational rental detail trả `hasPassingPreInspection` và `hasPostInspection` từ evidence của **xe hiện tại**, khớp điều kiện start/complete; kiểm tra của xe cũ không mở khóa bàn giao.

### Acceptance
- [x] Phân công lưu được và đọc lại đúng kỹ thuật viên, xe, người giao và thời gian.
- [x] Chặn người không phải Maintenance/đã khóa, ID sai, rental đã bắt đầu và thay phân công sau evidence.
- [x] Không thay thế kết quả kiểm tra bằng thao tác phân công; bằng chứng của xe cũ không mở khóa bàn giao/hoàn tất.
- [x] API tuân thủ envelope 6 trường, validation và quyền đọc/ghi; route mới có Swagger.

2026-10-10: `POST /api/media/inspection` cho cùng quyền Inspection upload JPEG/PNG/GIF/WebP tối đa 10 MB lên Cloudinary; dùng `secureUrl` trong `imageUrls` của API inspection hiện có. Upload không tự tạo hay sửa evidence.
