# FR-018 — Danh sách đơn thuê phục vụ chỉnh sửa và bàn giao

Ngày: 2026-10-16. Phạm vi: API đọc đơn khách hàng/vận hành dùng cho luồng sửa trước thuê và bàn giao.

- `GET /api/rentals` giữ các bộ lọc status/customerId/vehicleId/from/to, quyền Admin/Manager/CustomerService.
- `GET /api/rentals/mine` chỉ đọc đơn của khách hiện hành; tab phải thuộc upcoming/ongoing/completed/cancelled hoặc bỏ trống.
- HTTP và Application đều từ chối pagination sai bằng 400: page >= 1; pageSize 1..100, mặc định 1/20. Không clamp; offset dùng Int64.
- Danh sách trả items/page/pageSize/totalCount, tổng sau lọc, thứ tự ID giảm dần; trang ngoài phạm vi trả items rỗng.
- Bộ lọc bàn giao nội bộ `HandoverStatus`: pending = pre_inspection; completed = đã có started_at và renting/post_inspection/completed; all = cả hai. Lịch sử bàn giao thành công vẫn truy xuất được sau khi trả xe.

Acceptance: kiểm chứng HTTP auth/validation/envelope/Swagger; unit validation Application; integration tổng sau lọc/trang rỗng, biên pageSize và offset lớn.
