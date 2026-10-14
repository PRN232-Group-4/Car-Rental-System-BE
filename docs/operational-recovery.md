# API vận hành: O2/O4/O5/BE-24

## Migration và tương thích

Áp dụng `db/migrations/0011_operational_recovery.sql` bằng startup migration (Database:ApplyMigrations=true). `license_plate` dùng `ADD COLUMN IF NOT EXISTS`, không ghi đè biển số đã thêm. Không sửa Init.sql hoặc migration cũ. Thay đổi này chưa được áp dụng lên Neon bởi tác vụ phát triển.

Xe nhận/trả `licensePlate` trong JSON ở POST/PUT/GET `/api/vehicles` và GET `/api/vehicles/{id}`; nullable, tối đa 255 ký tự. Xe dùng để thay thế phải có `car_detail.seat_count` hợp lệ, cùng loại và đúng số chỗ. Không tự nâng hạng hay tự chọn xe; Manager chọn xe khả dụng. Không có xe cùng số chỗ thì API từ chối, không đổi giá.

## Lịch trực và phân công

Manager/Admin tạo ca:

```http
POST /api/drivers/shifts
Content-Type: application/json

{ "driverStaffId": 12, "startTime": "2026-10-05T01:00:00Z", "endTime": "2026-10-05T10:00:00Z" }
```

- PUT `/api/drivers/shifts/{id}` nhận cùng body; thêm `cancelled: true` để hủy.
- GET `/api/drivers/shifts?from=...&to=...&driverStaffId=12` xem ca trực. Driver chỉ đọc ca mình.
- POST `/api/rentals/{id}/assignments` với `{ "driverStaffId": 12 }`: Manager/Admin phân công sau pre-inspection đạt hoặc khi đang renting.
- GET `/api/drivers/schedule` vẫn là lịch chuyến, khác ca trực.
- Ca phải bao phủ chuyến (phần thời gian còn lại nếu đang renting), driver active/bằng còn hạn, không trùng chuyến. Không được sửa/hủy ca khiến chuyến đã giao không còn ca bao phủ.
- Dữ liệu cũ chưa có ca: Manager cần tạo ca trước lần phân công hoặc bắt đầu chuyến tiếp theo. Không tự suy diễn ca từ assignment cũ.

## Inspection và report

POST `/api/rentals/{id}/inspections/pre` và `/post` giữ body hiện tại. Response thêm `staffId`, `vehicleId`; server lấy staff từ đăng nhập. Pre fail giữ rental pending, khóa xe; post fail khóa xe. Không tự tạo maintenance, refund hay phí. Evidence bất biến theo rental/vehicle/type.

Staff tạo báo cáo riêng:

```http
POST /api/rentals/42/reports
Content-Type: application/json

{ "type": "damage", "description": "Phát hiện hư hại sau khi trả xe", "imageUrls": "https://example.com/evidence.jpg" }
```

`type`: damage hoặc breakdown. Driver chỉ báo rental đang được phân công; breakdown yêu cầu renting và khóa xe ngay. GET `/api/reports?rentalId=42`: Manager/Admin đọc tất cả; staff đọc report do mình tạo. Các URL ảnh là tham chiếu, backend không tải URL đó.

## Hỏng giữa đường → bảo trì → đổi xe/tài

1. Driver POST `/api/rentals/{id}/reports`, type=breakdown.
2. Manager POST `/api/reports/{reportId}/maintenance`:

   ```json
   { "assignedStaffId": 20, "facilityId": null, "scheduledDate": "2026-10-05" }
   ```

   Người nhận phải active, role Maintenance. Report chuyển open → assigned, lưu maintenance_id; maintenance lưu rental_id và xe hỏng.
3. Manager tạo ca cho tài xế mới. Inspector có role Maintenance/Admin kiểm tra xe thay thế:
   POST `/api/rentals/{id}/replacement-inspections/{vehicleId}` với body inspection, ví dụ `{ "isOk": true }`.
4. Manager POST `/api/rentals/{id}/replacements`:

   ```json
   { "vehicleId": 25, "driverStaffId": 15, "reportId": 9 }
   ```

   Xe mới phải khả dụng, cùng loại, đúng số chỗ, pre-inspection đạt; tài mới khác tài hiện tại và đang trong ca. Đổi xe + assignment + audit + resolve report trong một transaction. Không chạm giá snapshot, invoice, cọc hay cộng phụ phí đổi xe. Xe cũ tiếp tục bị khóa cho bảo trì.
5. Technician PUT `/api/maintenance/{id}` để cập nhật công việc được giao. Không tự đổi assignee. Completed bắt buộc isSafe=true; xe chỉ mở lại khi không còn công việc unsafe/đang mở và sự cố cần xử lý.

Trước chuyến, Manager cũng dùng replacement-inspection rồi replacement, bỏ reportId; driverStaffId có thể null cho thuê tự lái. Pre-inspection không đạt không còn hủy rental tự động.

Damage report có POST `/api/reports/{id}/resolve` cho Manager sau review (nếu không cần bảo trì) hoặc sau maintenance completed. Breakdown chỉ resolved bằng đổi xe/tài. Hoàn tiền dùng API payment riêng. Công nợ và quy tắc thu phí bồi thường chưa được tự động hóa trong luồng này.

## Kiểm chứng

Tests chạy trên PostgreSQL tạm riêng: biển số CRUD/search, inspection không tạo tiền/bảo trì, coverage/overlap ca trực, report ownership, bảo trì đúng role, giữ giá khi thay xe, exact seat count, rollback và tranh xe đồng thời. Database triển khai phải chạy migration 0011 trước API mới.
