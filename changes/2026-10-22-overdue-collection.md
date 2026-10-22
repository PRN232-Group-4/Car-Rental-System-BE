# Quá hạn và thu phí sau trả xe

Thêm status=overdue cho bộ lọc operational rentals, count/phân trang phía SQL và isOverdue boolean từ đồng hồ máy chủ. Giữ lifecycle renting trên cùng bản ghi để /return tiếp tục ghi giờ trả thực tế. Quá hạn không tạo thêm rental hoặc mutation từ GET; returned_at/history/settlement lưu dấu trả trễ. Xem FR-037 và ADR-011.

Staff tạo link PayOS cho hóa đơn overtime unpaid liên kết settlement completed tại POST invoices/{id}/staff-checkout. Mở rộng cash-confirmation cho phần thiếu sau quyết toán trước checkout_started_at, ghi audit người/thời điểm thu, ledger type overtime và method cash, invoice paid nguyên tử. Không nhận amount/paid/email từ FE, không phát hành order mới (kể cả thiếu legacy order), giữ idempotency. Cash/online dùng chung lock rental rồi invoice; online reservation chặn cash kể cả provider lỗi. Invoice detail có kind/canConfirmCash/canCreateStaffCheckout; UI đọc đúng quyền/trạng thái từ server. Tiền online chỉ ghi paid qua provider receipt đã xác minh.

Kiểm chứng: 109 tests liên quan đạt trên PostgreSQL kiểm thử riêng, gồm race, gửi lặp, sai amount, ownership, unpaid/paid/không có settlement, hourly/longterm, biên deadline, paging, HTTP role/envelope/Swagger, regression payment/lifecycle. Build Release đạt. Không thêm migration; dùng schema hiện có tới 0028. Chưa thử PayOS tiền thật, chưa deploy hoặc migrate DB ứng dụng.
