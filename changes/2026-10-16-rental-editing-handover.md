# Sửa đơn trước thuê và bàn giao xe — 2026-10-16

## Kết quả

- Thêm PUT `/api/rentals/{id}`: sửa xe/lịch/nhu cầu tài xế, chỉ pending/pre_inspection chưa started_at; chặn sau khi bắt đầu kể cả no-op. Chặn thay đổi khi có hợp đồng ký, kiểm định, assignment active hoặc cam kết thanh toán.
- Tính lại tiền ở server theo đơn giá/tỷ lệ snapshot, cập nhật rental/detail/booking/invoice unpaid trong cùng transaction. Subscription chỉ dịch lịch cùng xe/thời lượng trong hạn, không trừ giờ lại.
- Thêm GET `/api/handovers`, GET `/api/handovers/{rentalId}`, POST `/api/handovers/{rentalId}/confirm`. Danh sách pending/completed/all giữ lịch sử sau trả xe. Xác nhận gọi start, chuyển rental sang renting và ghi audit.
- Kiểm định pre/post hoạt động sau confirm/return; start xác minh lại xe hiện tại, evidence, assignment, GPLX, payment, availability/maintenance dưới lock. Assignment kiểm tra lại snapshot sau lấy lock để tránh ghi lịch cũ sau edit.
- Danh sách rental/mine validate ở HTTP và Application, trả 400 thay vì clamp; offset Int64. Sửa tên cột phone/avatar trong rental queries cho khớp schema.
- Chuẩn hóa BusinessRuleException vào envelope chung (400/404/409 + error.code); bỏ exception handler trùng trong Program. Bổ sung success messages cho rental lifecycle/queries/editing/handovers/assignments và Swagger pagination/response.
- Checkout ghi marker trước gọi PayOS để link đã phát hành không bị đổi giá bởi edit; giữ marker khi provider timeout.

## Migration và phạm vi

`0020_invoice_checkout_reservation.sql` thêm checkout_started_at. Lần nâng cấp đầu khóa sửa các invoice unpaid có sẵn vì bản cũ không ghi nhận link PayOS; replay không khóa invoice mới. Cần migration trước API mới. Không tự mở khóa các đơn legacy khi chưa đối soát link ngoài. Không đổi customer/type/policy/subscription, không thêm trạng thái lifecycle hoặc bảng handover riêng. FE chưa thay đổi; không deploy hoặc sửa database thật.

## Kiểm chứng

- Build khi chạy test: `dotnet test ... --no-restore --disable-build-servers -m:1 -warnaserror` đạt, 0 warning/error.
- **236/236** test chọn lọc đạt, 0 skip: HTTP JSON 6 trường/phân quyền/validation/Swagger; Domain/Application lifecycle/editing/handover/checkout; kiến trúc; PostgreSQL editing, invoice/booking, overlap rollback, stale assignment, concurrent handover, maintenance/payment guards, legacy migration và các regression lifecycle/assignment/payment.
- PostgreSQL **18** tạm trên localhost:55432, timezone UTC, database ngẫu nhiên do fixture tạo rồi xóa; không dùng database của ứng dụng. PostgreSQL16/Docker/PayOS thật chưa kiểm chứng.
- Kết quả: `artifacts/test-results/rental-edit-handover/final.trx`. Git diff --check sạch. PostgreSQL tạm dừng và dữ liệu cluster tạm được dọn sau test.

## Acceptance

- [x] Có thể liệt kê/lọc đơn để chọn ID; phân trang đúng contract.
- [x] Sửa đơn trước thuê; sau start và các trạng thái kết thúc trả409.
- [x] Booking và hóa đơn đồng bộ nguyên tử, không đổi link thanh toán đã phát hành.
- [x] Có danh sách/chi tiết/xác nhận bàn giao; kiểm định sau confirm hoạt động.
- [x] Bàn giao lặp/đồng thời không ghi start hai lần; evidence của xe cũ không hợp lệ.
- [x] Specs/API/ADR/progress/assumptions/changelog được cập nhật.
