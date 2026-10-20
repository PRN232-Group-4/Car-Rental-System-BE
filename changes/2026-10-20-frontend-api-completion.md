# Hoàn thiện backend theo danh sách API/field thiếu — 2026-10-20

- Quản trị tài khoản: users directory/detail, status+reason, role membership, session revocation; full roles/customerId/staffId, audit, last-admin protection, authorization-version invalidation.
- Dashboard có DTO/schema; revenue-series, top-vehicles, maintenance-vs-revenue, plan-usage; so sánh kỳ trước, lọc facilityId/vehicleTypeId. Xem change dashboard analytics để biết định nghĩa số liệu.
- Báo cáo sự cố: tiếp nhận/từ chối, thông tin khách/đơn/bảo dưỡng, timestamp và lịch sử thật. Ratings: kiểm duyệt và phản hồi quản lý, phân trang, chỉ đánh giá visible tham gia điểm trung bình. Maintenance bổ sung createdAt/rentalId và tùy chọn nội dung khi giao từ report.
- Customer rental list/detail: contact cơ sở có ID/name/phone, biển số/loại xe, action/quyền hủy nhất quán, giờ dùng theo đơn subscription. `facility` trong detail đổi từ string thành object nullable; pickup.location tiếp tục giữ tên cơ sở.
- Catalog list cung cấp thông số kỹ thuật thật, supportsSubscription, driverAvailable nullable, features[] có lưu/nhập qua create/update. Policy có typeName/managedBy/createdAt; driver assignment/schedule có tên xe/biển số/buffer.
- Facilities bổ sung hotline có thể cập nhật qua create/update và trả danh sách phân trang.
- Thêm migration 0022/0023/0024; không sửa baseline hay backfill dữ liệu giả. Build và HTTP/PostgreSQL tests được ghi sau kiểm chứng tại docs/progress.md.

Chưa deploy hoặc chạy migration vào database ứng dụng. FE phải đọc list qua items/page/pageSize/totalCount; các danh sách ratings/reports/maintenance/driver schedule/facilities đã chuẩn hóa pagination.

Kiểm chứng cuối: build -warnaserror đạt, 325/325 tests liên quan đạt (PostgreSQL18 tạm, HTTP thật, unit/concurrency/Swagger). Log TRX: `.validation/api-completion-results/api-completion-final.trx` ở workspace cha. Không chạy toàn bộ suite/Docker hoặc deploy.
