# ADR-011 — Phân loại quá hạn và thu phí độc lập với trả xe

Ngày: 2026-10-22. Trạng thái: chấp nhận trong source theo yêu cầu bổ sung luồng Admin.

Quá hạn phụ thuộc đồng hồ và thời điểm trả thực tế. Giữ lifecycle `renting` trong DB và trả `isOverdue`/lọc `status=overdue` cho read model, hiển thị Quá hạn. Không tạo đơn thứ hai, không mutation khi GET, không cần scheduler để giữ status đồng bộ. Sau trả xe, returned_at/history/settlement lưu bằng chứng trễ lâu dài. Admin vẫn gọi `/return` rồi kiểm tra sau thuê và `/complete`.

Phần tiền còn thiếu là hóa đơn overtime liên kết settlement; completed không có nghĩa mọi phụ phí đã paid. Thu cash và tạo PayOS checkout dùng cùng lock order rental rồi invoice. Cash chỉ được chọn trước checkout_started_at và ghi người thu; sau marker phải tiếp tục online, kể cả provider timeout, nhằm tránh thu trùng khi chưa biết trạng thái provider. Không tự reset marker hoặc hủy link. Chỉ webhook đã xác minh mới ghi online paid. Ledger dùng type overtime, không phát hành order mới.

Giới hạn: tạo link online sử dụng tích hợp PayOS hiện hữu; chưa thử giao dịch tiền thật. Không thêm job chuyển lifecycle sang overdue hoặc bảng sao chép đơn quá hạn. Source cần được triển khai cùng FE; không triển khai trong yêu cầu này.
