# ADR-008 — Duyệt yêu cầu trước thanh toán và phát hành đơn thuê

- Ngày: 2026-10-18
- Trạng thái: Accepted, theo xác nhận trực tiếp của người dùng

## Quyết định

Customer gửi yêu cầu đặt xe và chọn `payos` hoặc `cash` khi thuê trực tiếp. Staff theo policy CustomerService duyệt trước khi khách được thanh toán. `confirmed_at` là bằng chứng duyệt; trạng thái `pre_inspection` do kiểm định legacy tự chuyển không thay thế bằng chứng này. Bàn giao vẫn yêu cầu hóa đơn thuê ban đầu đã thanh toán.

Giữ `rentals` làm hồ sơ yêu cầu, giữ chỗ xe và vận hành để tương thích các API, assignment, inspection và settlement hiện có. Thêm `rental_orders` với mã riêng và `rental_id` duy nhất làm đơn thuê chính thức. Duyệt yêu cầu trực tiếp chưa thanh toán không phát hành order. Với gói đã trả trước, phát hành khi staff duyệt, không tạo hóa đơn thuê mới.

Webhook PayOS phải được xác minh chữ ký, số tiền và reference. Thu tiền mặt chỉ do Admin/Manager/CustomerService xác nhận thực nhận đúng toàn bộ số tiền từ hóa đơn server, có audit người thu. Ghi ledger paid, invoice paid và phát hành order trong cùng transaction. Khóa rental rồi invoice thống nhất giữa các đường thanh toán để xử lý gọi lặp/đồng thời; unique rental_id bảo đảm một order. Không tin trạng thái trên URL frontend. Khoản đã nhận từ link legacy trước duyệt vẫn phải ghi ledger, nhưng chờ staff duyệt mới phát hành order. Không phát hành order mới cho yêu cầu hủy hoặc hóa đơn overtime.

## Hệ quả

Cần migration `0021_rental_orders_and_cash_payment.sql` trước khi dùng backend mới; hosted migrator tự đọc các file SQL theo thứ tự. Backfill chỉ những rental có bằng chứng duyệt và paid ledger hoặc gói trả trước, không dựng đơn từ invoice unpaid. API detail invoice thêm `canPay`, `paymentBlockedReason`, `paymentMethod`, `orderId`; API rental hiện hành thêm `orderId` nullable. `id` rental tiếp tục là mã yêu cầu/vận hành, không phải mã order. List invoice giữ contract hiện tại.

Frontend hiển thị yêu cầu đang chờ duyệt, mở PayOS sau duyệt hoặc chờ staff thu tiền mặt; chỉ hiển thị đơn chính thức khi server trả orderId. Chưa thêm cơ chế payout hoàn tiền thật hoặc giữ chỗ tài xế trước phân công.
