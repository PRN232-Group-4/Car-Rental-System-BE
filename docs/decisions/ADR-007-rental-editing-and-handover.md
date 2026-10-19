# ADR-007 — Sửa đơn trước thuê và xác nhận bàn giao

- Ngày: 2026-10-16
- Trạng thái: Accepted

## Quyết định

Sửa các điều khoản xe/lịch/nhu cầu tài xế qua PUT rental chỉ khi pending/pre_inspection và chưa started_at. Không thay customer, rental type, policy hoặc subscription; không ghi đè hợp đồng, kiểm định, phân công hay giao dịch tài chính đã cam kết. Tính lại giá trên server, cập nhật booking và invoice unpaid trong cùng transaction. Các snapshot và tỷ lệ VAT/cọc của đơn được giữ khi đổi lịch; đổi xe dùng đơn giá xe mới, kiểm tra loại xe của policy.

Bàn giao là một góc nhìn của rental lifecycle, dùng rentalId làm định danh. Hàng chờ là pre_inspection; xác nhận thành công gọi start, chuyển renting và ghi started_at/audit. Lịch sử bàn giao đã thực hiện tiếp tục tồn tại ở renting/post_inspection/completed. Không thêm bảng hoặc enum lifecycle cạnh tranh với rentals.

Giữ tương thích đường kiểm định legacy, đồng thời cho phép ghi kiểm định sau confirm và return. Start kiểm tra lại trạng thái, xe hiện tại, inspection, assignment, GPLX, payment và availability trong transaction. Dispatcher/vehicle/rental locks đồng bộ với sửa đơn; assignment phải kiểm tra lại snapshot sau khi lấy lock.

Checkout phải ghi checkout_started_at và commit trước gọi PayOS. Marker khóa sửa điều khoản kể cả provider timeout, vì request ngoài có thể đã tạo link. Không tự bỏ marker khi redirect/hủy; thay đổi các đơn đã phát hành checkout cần một workflow tài chính khác. Invoice đọc thông thường không tạo marker. Khi nâng cấp lần đầu, migration đánh dấu các invoice unpaid có sẵn vì phiên bản cũ không lưu bằng chứng phát hành link, không thể phân biệt an toàn; chỉ backfill một lần, invoice mới không bị đánh dấu khi replay migration.

## Hệ quả

Một số đơn chưa renting vẫn không sửa được do đã có bằng chứng hoặc checkout; trả 409 với mã cụ thể thay vì âm thầm sửa giá/link/hồ sơ. Subscription chỉ dịch lịch giữ xe và duration. Cần áp dụng migration 0020 trước dùng API mới. Giữ response envelope và pagination chung; HTTP và Application đều validate.
