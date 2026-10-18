# FR-036 — Yêu cầu đặt xe, duyệt và phát hành đơn thuê đã thanh toán

- Người dùng xác nhận 2026-10-18: tách yêu cầu đặt xe và đơn thuê chính thức; staff duyệt trước thanh toán. Ghi đè thứ tự thanh toán trước confirm trong FR-018 cũ.
- Tạo yêu cầu hourly/longterm lưu phương thức payos hoặc cash và giữ lịch xe; chưa có rental_orders. Giữ API rental IDs cho hồ sơ yêu cầu/vận hành hiện tại, order có mã riêng liên kết rental_id.
- Staff theo policy CustomerService duyệt, ghi confirmed_at. Đơn trực tiếp vẫn chưa có order khi invoice chưa paid. Gói trả trước tạo order khi staff duyệt, không thêm hóa đơn.
- Online: chỉ tạo checkout PayOS cho invoice payos đã duyệt. Webhook chữ ký/amount hợp lệ ghi payment paid, invoice paid và order trong cùng transaction. Redirect/status=PAID từ frontend không đủ.
- Cash: customer không thể tự xác nhận; Admin/Manager/CustomerService xác nhận thực nhận toàn bộ số tiền invoice tại endpoint cash-confirmation, có audit người thu. Trước duyệt hoặc sai phương thức/cancelled bị chặn. Ledger, invoice paid và order nguyên tử.
- Callback PayOS/thu tiền mặt lặp hoặc đồng thời chỉ tạo một payment và một order. Không tự mở order chỉ vì khách chọn phương thức hoặc staff duyệt đơn trực tiếp.
- Hóa đơn overtime không sinh order thứ hai. Không tự phát hành order cho yêu cầu đã hủy. Khoản PayOS có bằng chứng nhận tiền trước staff duyệt từ link cũ vẫn ghi ledger; order chỉ cấp khi đủ cả xác nhận và paid.
- Kiểm chứng HTTP phân quyền/envelope/validation, Application/Domain, PostgreSQL đồng thời và UI customer/staff. Không dùng PayOS hoặc tiền thật để test.
- Danh sách/chi tiết thuê của Admin và customer trả `orderId` nullable từ cùng bản ghi rental_orders; `id` vẫn là mã yêu cầu. Không tự suy diễn order đã phát hành từ status. Admin không truyền status xem mọi trạng thái qua phân trang hiện có; chi tiết giữ timeline/audit cho thanh theo dõi.

## Acceptance đã kiểm chứng

- [x] Tạo/duyệt yêu cầu trực tiếp chưa thanh toán chưa tạo order; khóa checkout/thu tiền trước duyệt hoặc khi hủy.
- [x] PayOS sai chữ ký/số tiền không tạo order; callback hợp lệ/lặp/đồng thời ghi một ledger và order paid.
- [x] Staff xác nhận tiền mặt có audit/số tiền server; customer/role không được phép bị chặn; thao tác lặp/đồng thời idempotent.
- [x] Gói trả trước phát hành khi duyệt; tiền legacy trước duyệt chờ duyệt; migration replay không đổi mã order.
- [x] Invoice detail/envelope/Swagger canPay boolean, orderId nullable; customer/staff UI kiểm tra trạng thái server, URL PAID giả không báo thành công.
- [x] Bàn giao trực tiếp vẫn yêu cầu paid; legacy pre_inspection chưa confirmed_at không mở thanh toán.
- [x] Rental list/detail customer/vận hành trả đúng orderId trước/sau nhận tiền; Admin list không lọc giữ cả trạng thái completed/cancelled, paging/envelope/Swagger được kiểm chứng (44 test liên quan cho thay đổi danh sách/tracking).

91 test backend chọn lọc và 24 E2E frontend đạt, không tuyên bố toàn bộ backend suite sạch; xem giới hạn trong change 2026-10-18-booking-request-paid-order.
