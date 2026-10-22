# Kiểm tra kèm duyệt yêu cầu cập nhật tiến trình thật

Nguyên nhân: checked chỉ lưu evidence, trong khi pending là yêu cầu chưa duyệt. Thêm approveRequest boolean mặc định false tại POST checked (Admin only). Explicit true lưu phân công/pass/confirmed_at/history và chuyển pre_inspection nguyên tử; tách RentalApprovalWriter dùng chung với confirm, giữ chính sách order paid/subscription. Không tự start/paid, không tự duyệt từ driver_default/GET/phân công đơn thuần. Evidence đã có không bị ghi đè; đơn pending đã pass dùng confirm riêng.

Kiểm chứng: 58 backend tests liên quan đạt trên PostgreSQL riêng, gồm HTTP role/envelope/Swagger boolean, hourly/longterm approval thật, legacy/default không tự duyệt, gói tạo order đúng chính sách, không thu tiền/không tự bàn giao và regression lifecycle/paid order. Build Release đạt. Không thêm migration mới, chưa deploy/migrate DB ứng dụng.
