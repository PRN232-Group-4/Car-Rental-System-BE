# FR-011 — Rental policy
- **Status:** In progress; **Priority:** P1; **Milestone:** M3

Admin và Manager cấu hình VAT/overtime/deposit bằng cách tạo policy version mới. Điều khoản policy không được sửa trực tiếp; được bật/tạm ngưng riêng trạng thái `isActive`. Chỉ policy active được áp dụng cho đơn mới và rental giữ snapshot giá tại thời điểm đặt.

| Method | Endpoint | Roles | Mô tả |
|---|---|---|---|
| GET | `/api/rental-policies?page=1&pageSize=20` | Admin, Manager | Danh sách phân trang, có số đơn thuê `rentalCount`, gồm cả policy đã ngừng áp dụng. |
| POST | `/api/rental-policies` | Admin, Manager | Tạo một policy version mới. |
| PATCH | `/api/rental-policies/{id}/status` | Admin, Manager | Đặt trạng thái bằng body `{ "isActive": true/false }`, trả policy sau cập nhật. |

Không có API sửa điều khoản hoặc xóa rental policy. Khi cần đổi điều khoản, tạo policy mới; policy cũ vẫn được giữ làm lịch sử cho các rental đã tạo. Ngoại lệ duy nhất là PATCH trạng thái theo yêu cầu ngày 15/10/2026; xem ADR-006.

## Trạng thái chính sách

- `id` phải dương, `isActive` bắt buộc là JSON boolean (không chấp nhận thiếu/null/string/number). Sai trả 400; không tồn tại trả 404; chưa đăng nhập 401, sai quyền 403. Mọi response dùng envelope 6 trường.
- Chỉ cập nhật `is_active` của bản ghi đích; giữ nguyên loại xe, tên, VAT, phí trễ, đặt cọc và toàn bộ rental/invoice/snapshot đã có. Không tạo phiên bản mới khi chỉ đổi trạng thái.
- Request đặt trạng thái tường minh, idempotent; đặt lại cùng giá trị vẫn trả 200. Không tự đổi trạng thái các policy khác. Nếu nhiều policy active cùng phạm vi, quy tắc chọn bản mới nhất hiện tại vẫn áp dụng (ưu tiên loại xe, sau đó bản chung).
- FE hiển thị icon tạm ngưng/kích hoạt ở cột Thao tác, có tooltip và tên truy cập. Xác nhận trước gửi; khóa gửi lặp trong lúc chờ. Chỉ đổi badge khi API thành công; lỗi giữ trạng thái trước và cho thử lại.

## Acceptance criteria cho đổi trạng thái

- [x] Admin/Manager bật và tạm ngưng được policy, quyền khác bị chặn (HTTP test).
- [x] False được chấp nhận, thiếu/null/sai kiểu và ID không hợp lệ bị từ chối; ID không tồn tại trả 404.
- [ ] Lặp cùng request không tạo bản ghi mới; các điều khoản và giá của đơn cũ không đổi.
- [x] Swagger và tài liệu mô tả request, quyền, response; JSON HTTP có đủ 6 trường và boolean đúng kiểu.
- [x] FE desktop/mobile chỉ hiện icon, xử lý xác nhận, loading, thành công/lỗi và gửi đúng ID/trạng thái.

Kiểm chứng PostgreSQL cho snapshot và persistence đã có test nhưng chưa chạy với database hợp lệ; cấu hình test mặc định hiện bị từ chối xác thực. Chưa triển khai môi trường thật.

## Kiểm tra loại xe và số lần thuê — 2026-10-15

- Khi tạo policy, `vehicleTypeId` null vẫn là áp dụng chung. Nếu có ID thì phải là số dương và còn tồn tại trong `vehicle_types`. Application kiểm tra tham chiếu; repository kiểm tra lại dưới khóa hàng trong cùng transaction trước INSERT để không mất loại xe giữa kiểm tra và lưu. Loại xe không hợp lệ trả 400 với `error.errors.VehicleTypeId`, để FE hiển thị inline và giữ form.
- `name` bắt buộc, ba tỷ lệ từ 0 đến 100 được validate ở Application ngoài HTTP DTO. Theo xác nhận người dùng, chặn tạo mới nếu đã có policy active cùng `vehicleTypeId` (null là phạm vi chung riêng); lỗi 400 gắn trường VehicleTypeId. Tạm ngưng bản cũ trước khi tạo. Kích hoạt lại cũng bị chặn khi có bản active khác cùng phạm vi; đặt lại chính bản active vẫn idempotent. Repository tuần tự hóa ghi theo phạm vi bằng transaction advisory lock để ngăn request đồng thời vượt kiểm tra. Không tự sửa dữ liệu active trùng đã có.
- `rentalCount` là tổng bản ghi `rentals` có `policy_id` bằng ID policy, gồm mọi trạng thái (kể cả chờ/hủy), mọi loại thuê và đơn subscription có gắn policy. Không cộng theo loại xe, không đếm invoice, không suy ra policy cho đơn có `policy_id` null. Policy chưa dùng trả 0.
- Danh sách trả `items/page/pageSize/totalCount` trong envelope; mặc định 1/20, page dương, pageSize 1–100, validate HTTP/Application, thứ tự ID giảm dần. Offset dùng số 64 bit; trang vượt phạm vi vẫn có tổng đúng. `rentalCount` là số nguyên 64 bit; POST trả 0, PATCH status trả số đếm hiện tại để không mất số trên bảng.
