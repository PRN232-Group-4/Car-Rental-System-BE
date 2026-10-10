# ADR-003 — Pagination cho API danh sách

- **Ngày:** 2026-10-10
- **Trạng thái:** Accepted

## Bối cảnh

Người dùng yêu cầu một rule chung cho mọi API trả danh sách, tránh contract khác nhau giữa các endpoint và giới hạn số bản ghi trong mỗi response.

## Quyết định

Mọi API list nhận `page` mặc định 1 và `pageSize` mặc định 20. `page >= 1`; `pageSize` từ 1 đến 100. HTTP và Application đều validate, trả 400 khi sai và không tự clamp.

Trong envelope API hiện tại, `data` có đủ `items`, `page`, `pageSize`, `totalCount`. Tổng được tính sau lọc, trước phân trang. Query có thứ tự ổn định và offset không tràn số nguyên; trang ngoài phạm vi trả items rỗng cùng metadata chính xác. Swagger/tài liệu/test mô tả và kiểm chứng contract.

## Hệ quả

API list mới hoặc được sửa phải đáp ứng rule. Customer/staff/vehicle hiện có page contract; các API cũ còn trả array cần chuyển đổi, đồng thời cập nhật client, Swagger và test. Lượt thay đổi này chỉ ghi rule, chưa chuyển đổi các endpoint cũ.
