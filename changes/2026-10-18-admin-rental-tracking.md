# Danh sách Admin và chi tiết theo dõi đơn thuê — 2026-10-18

- Tái sử dụng GET /api/rentals và GET /api/rentals/{id}/detail cho danh sách mọi trạng thái và xem chi tiết. Không truyền status bao gồm cả completed/cancelled. Giữ policy CustomerService, pagination page=1/pageSize=20 tối đa100, envelope sáu trường, filter và thứ tự ID giảm dần.
- Thêm orderId nullable vào summary và detail của customer/vận hành. Mã từ rental_orders độc lập với mã yêu cầu id; không suy diễn paid/order từ trạng thái. Không thêm endpoint hoặc migration mới; dùng migration0021 đã có.
- Timeline/history và timestamps hiện có phục vụ UI tracking, enum JSON operational detail giữ nguyên để tránh phá contract.
- Cập nhật FR-036, API docs và Swagger mô tả orderId; thêm kiểm chứng PostgreSQL về projection trước/sau nhận tiền, all-status scope và HTTP envelope/null/Swagger.
- Kiểm chứng: build --no-restore -warnaserror đạt (0 warning/error); 44/44 test PaidOrderIntegrationTests, RentalQueryPaginationIntegrationTests, RentalOperationsApiTests, RentalQueryValidationTests đạt với PostgreSQL18 tạm và HTTP thật localhost. Kiểm tra envelope sáu trường/orderId null/Swagger nullable integer, phân quyền, pagination/default/bounds/empty pages và all-status scope. Không đổi dữ liệu thật hoặc deploy.
