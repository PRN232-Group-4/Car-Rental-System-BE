# FR-028 — Email Brevo
- **Status:** In progress; **Priority:** P0; **Milestone:** M7
OTP, subscription reminder và operational notification bên dưới đã gửi qua Brevo REST; invoice issuance/receipt độc lập còn chưa làm. Acceptance: [x] API key config; [ ] outbox/retry.

## 2026-10-21 — Thông báo phê duyệt và tình trạng đơn thuê

- Sau khi lưu thành công, gửi email tới khách của GPLX khi approved/rejected; tới khách của rental khi confirm/start/return/complete/cancel, gồm alias status và handover dùng chung lifecycle.
- Duyệt/từ chối hoàn tiền của đơn thuê gửi khách; lưu kết quả kiểm tra trước/sau thuê gửi khách kết quả đạt/không đạt; recovery gửi cập nhật đổi xe.
- Khi staff/driver tạo báo cáo sự cố hoặc staff tiếp nhận/từ chối/giao bảo trì/giải quyết, gửi khách của đơn thuê và nhân viên tạo báo cáo. Recovery giải quyết breakdown cũng gửi thông báo resolved.
- Chỉ gửi sau repository đã hoàn tất commit/autocommit. Email/tra cứu người nhận/timeout bị lỗi không rollback, không đổi kết quả thành lỗi; lỗi nghiệp vụ gốc vẫn trả nguyên trạng và không gửi email.
- Nội dung chỉ gồm mã đơn/báo cáo, trạng thái và hướng dẫn xem chi tiết. Không gửi mô tả sự cố, bằng lái, ảnh hoặc dữ liệu nội bộ nhạy cảm. Log chỉ loại sự kiện, ID và loại exception, không log địa chỉ/response provider/API key.
- Dùng Brevo config hiện có và thời gian chờ giới hạn 10 giây cho lần gửi best effort; không phụ thuộc request còn mở sau commit. Không cam kết delivery/retry/outbox, không gửi lại email từ GET/list. OTP giữ chính sách lỗi hiện có.
- Acceptance: [ ] review và lifecycle gửi đúng người/trạng thái sau lưu; [ ] report create/review/assign/resolve gửi đúng người; [ ] provider/lookup/cancellation failure không làm lỗi nghiệp vụ thành công; [ ] lỗi nghiệp vụ không gửi; [ ] HTTP Brevo lỗi không lộ thông tin provider.
