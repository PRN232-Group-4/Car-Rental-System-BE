# Chuyển truy vấn đọc sang Entity Framework và gộp file — 2026-10-25

## Kết quả

- Dùng EF Core 8/provider PostgreSQL trong Infrastructure. Context no-tracking, 46 bảng và 3 view ánh xạ schema hiện có, không tạo foreign key/migration.
- Chuyển đọc danh sách/chi tiết/tồn tại/quyền ở User, Account, Customer, Staff, UserAdministration, Role, Audit, Inspection, Operations, Assignment, Availability, RentalQuery/Lifecycle, Payment, Loyalty, Dashboard, Tracking và thông báo sang LINQ.
- Giữ filtering/count/order/pagination ở PostgreSQL; dashboard aggregate trước materialize. Giữ snapshot bằng RepeatableRead ở các truy vấn nhiều bước cần nhất quán. EF có thể dùng chung connection/transaction Npgsql.
- SQL vẫn tồn tại cho ghi/RETURNING, advisory/row lock và kiểm tra nghiệp vụ nội bộ trong transaction ghi. Không tuyên bố đã bỏ toàn bộ SQL.
- Tách hai lệnh lưu hợp đồng thành hai command trong cùng transaction sau khi integration phát hiện lỗi prepared statement khi chạy cùng EF/Npgsql. Regression xác nhận upload/download và thay thế tài liệu vẫn nguyên tử.
- Gộp 12 file nhỏ cùng trách nhiệm. Application 129 → 120 file `.cs`; Infrastructure 54 → 53 sau khi thêm hai file EF. Tổng `src` **246 → 236**; hai file test EF được bổ sung ngoài thống kê `src`.
- Giữ nguyên namespace/public types/API và ranh giới layer. Frontend được rà soát; không sửa.
- Rà soát lần cuối và chuyển helper Staff đọc profile/role/reference sang EF trong cùng transaction; xóa đăng ký DbContext không dùng trong Program (repository dùng factory theo thao tác).
- Đồng bộ tài liệu quyền staff/media với quyết định Admin/Manager đã duyệt ngày 21/10; không đổi quyền production.

## Kiểm chứng

- Build solution `--no-restore -m:1 -warnaserror`: đạt, 0 warning/error.
- Publish WebApi Release `--no-restore -m:1 -warnaserror`: đạt.
- Toàn bộ backend sau hoàn tất: **1.248/1.248 pass, 0 fail, 0 skipped** trên PostgreSQL 18 tạm, session UTC. Ba ca quyền bị từ chối được bổ sung.
- Trước khi sửa fixture, đã chạy riêng HEAD gốc `e4723e2227fef4e1c4dd1f1136877755473cab7f`: hai lượt 54 và 26 test xác nhận đủ 12 lỗi cũ cùng tên/nguyên nhân. Các lỗi này đã được sửa đúng nguyên nhân, không skip hoặc nới validation sản phẩm.
- Test EF bổ sung: tất cả bảng/view query được sau migration; no-tracking/không FK; dùng chung transaction và ownership connection; tái sử dụng model qua nhiều data source; quyền truy cập thuê/hợp đồng; GPS/history/nearby/geofence/alert/null/trang rỗng.
- `git diff --check`: đạt.
- Đã thử khởi động Docker Desktop qua CLI và executable nền; Linux engine vẫn không mở named pipe `dockerDesktopLinuxEngine`, nên build container chưa thực hiện được. Publish Release đạt. Không thay cấu hình Docker/WSL toàn máy.

### Đã xử lý 12 lỗi có sẵn

| Nhóm | Số ca | Sửa fixture/test |
|---|---:|---|
| RentalLifecycleIntegration | 3 | Hạn GPLX phủ thời gian chuyến giả lập năm 2032 |
| SubscriptionRental | 2 | GPLX đã duyệt cho tự lái; kiểm tra thiếu giờ assert đúng nguyên nhân |
| HandoverIntegration (longterm) | 2 | Lưu PDF hợp đồng thật trước khi bàn giao, không dùng URL thay evidence |
| CustomerDetail | 1 | Seed rõ metadata GPLX null cho trường hợp kiểm tra null |
| CustomerLicenseUpload (Manager staff-license) | 1 | Theo quyền Management đã duyệt 21/10; thêm kiểm tra bytes/folder/URL/không lưu GPLX và 3 role bị từ chối |
| SignedRentalContract (filename) | 1 | FileNameStar gửi nguyên filename độc hại tới server, giữ assertion sanitize/download |
| VehicleBookingMigration | 2 | Chọn chính xác migration booking thay prefix 0015 trùng |

Kết quả lượt cuối: `tests/VehicleRental.Tests/TestResults/ef-completion.trx`. Bằng chứng baseline: `artifacts/ef-baseline-results/` (Git ignore). Database ứng dụng không được migrate/ghi dữ liệu; không deploy.

## Tài liệu

- [ADR-012](../docs/decisions/ADR-012-entity-framework-read-queries.md)
- [NFR-001](../specs/NFR-001-clean-architecture.md)
- [Rà soát gộp file](../docs/file-consolidation-review.md)
