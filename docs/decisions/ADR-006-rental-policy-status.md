# ADR-006 — Cho phép đổi trạng thái chính sách thuê xe

- Ngày: 2026-10-15
- Trạng thái: Accepted theo yêu cầu người dùng bổ sung nút đặt lại status.

Điều khoản chính sách vẫn lưu theo phiên bản và không được sửa/xóa. Bổ sung `PATCH /api/rental-policies/{id}/status` cho Admin/Manager để đặt riêng `isActive` bằng boolean bắt buộc. Cập nhật nguyên tử một cột, trả bản ghi sau cập nhật; cùng trạng thái vẫn thành công, không tạo phiên bản mới.

Không đổi trạng thái bản khác, không thay thứ tự ưu tiên chọn policy (active mới nhất theo loại xe rồi fallback chung), không thay snapshot hay số tiền của rental/invoice cũ. Hai request đồng thời dùng trạng thái được ghi cuối cùng. Chưa bổ sung ràng buộc chỉ một policy active vì không thuộc yêu cầu này.

Quyết định này thay phần cấm mọi PATCH trong FR-011 trước đây; cấm sửa điều khoản và xóa vẫn giữ nguyên. Không thay API danh sách trong thay đổi status ban đầu.

## Bổ sung theo xác nhận người dùng ngày 15/10/2026

Chặn tạo mới khi có policy active cùng loại xe; null là phạm vi chung riêng. Cũng chặn kích hoạt một bản inactive nếu có bản khác active cùng phạm vi. Tạm ngưng bản cũ trước khi tạo/kích hoạt bản mới; không tự tắt các phiên bản khác. Ghi mới và bật trạng thái được tuần tự hóa bằng transaction advisory lock theo phạm vi. Dữ liệu active trùng đã có giữ nguyên; đặt lại true trên chính bản đang active vẫn idempotent, lookup vẫn ưu tiên bản mới nhất cho dữ liệu cũ.

GET danh sách nay có pagination và rentalCount theo yêu cầu bổ sung cột số lần thuê. Xem FR-011 và change 2026-10-15-rental-policy-validation-usage.
