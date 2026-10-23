# Số lượt thuê theo ngày trong chuỗi doanh thu — 2026-10-23

## Bối cảnh

Dashboard cần biểu đồ “số lượt thuê theo thời gian” cạnh biểu đồ doanh thu. Trước đây `GET /api/dashboard/revenue-series` chỉ trả tiền, nên FE phải gọi `GET /api/rentals` nhiều lần cho từng tab trạng thái — sai bản chất (đơn *đặt* khác đơn *bắt đầu*) và tốn N request cho mỗi lần đổi bộ lọc.

## Đã làm

- `RevenueSeriesEntry` thêm hai trường tuỳ chọn ở cuối record (không phá vị trí tham số cũ):
  - `Rentals`: số chuyến **thực tế bắt đầu** trong bucket (`started_at`), bỏ đơn `cancelled`.
  - `PreviousRentals`: cùng chỉ số đó ở kỳ liền trước (cùng phép dịch kỳ đang dùng cho `previous_total`).
- `NpgsqlDashboardRepository.RevenueSeriesAsync`: hai subquery đếm trên `scoped_rentals` (đã áp bộ lọc cơ sở/loại xe sẵn có) — không thêm round-trip, không bảng mới, không migration.
- `docs/API.md`: mô tả lại dòng `revenue-series`.

## Kiểm chứng

- `dotnet build` 0 warning/0 error.
- `dotnet test --filter "FullyQualifiedName~Dashboard"`: 24/24 đạt (gồm `DashboardIntegrationTests` kiểm bucket/biên kỳ và `DashboardApiTests` kiểm envelope/Swagger).
- `dotnet test` toàn bộ: 1208 đạt / **29 lỗi có sẵn từ trước** trên `dev` (18 `SignedRentalContractTests`, 3 `RentalLifecycleIntegrationTests`, 2 `SubscriptionRentalTests`, 2 `VehicleBookingMigrationTests`, 2 `HandoverIntegrationTests`, 1 `CustomerDetailTests`, 1 `CustomerLicenseUploadTests`) — y hệt danh sách baseline, không có test Dashboard nào lỗi.

## Giới hạn

- Đếm theo **lượt bắt đầu** (`started_at`), không phải lượt đặt. Đơn bị huỷ sau khi đã bắt đầu vẫn không bị đếm lại; đơn huỷ trước khi bắt đầu không xuất hiện trong biểu đồ — đúng ý “số lượt thuê đã diễn ra”.
- Chưa đổi `DashboardMetrics` (snapshot) để tránh phá hợp đồng đang dùng; số liệu snapshot vẫn là `revenue/renting/maintenance/activeSubscriptions`.
