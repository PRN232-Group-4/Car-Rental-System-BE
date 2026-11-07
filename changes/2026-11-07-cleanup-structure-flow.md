# 2026-11-07 — Dọn code không dùng, thống nhất cấu trúc và tài liệu luồng
- **FR/NFR:** —
- **Loại:** chore | docs

## Đã làm
- Xoá type không có tham chiếu:
  - `Domain/Entities`: GeofenceEvent, TrackingAlert, TrackingDevice, VehiclePosition.
  - Các type đơn lẻ: `ForbiddenException`, `AdministrativeRoles`, `LoyaltyTransactionType`, `LoyaltyTransactionTypeExtensions`, `LoyaltyCustomerSummary`.
  - Hai helper `Sql.Typed` và `Sql.Bound`.
- Xoá port chết cùng đăng ký DI: `ILoyaltyRedemption` (gồm `LoyaltyService.QuoteAsync`/`Validate`) và `IMaintenanceIncidentGateway`. Method `OpenIncidentMaintenanceAsync` chuyển thành private.
- Xoá method chỉ khai báo mà không có ai gọi:
  - `IOperationsService`: ListReports, GetRental, ListDriverSchedule, BuyPlan, ExpireSubscriptions, ListSubscriptionsToRemind, MarkSubscriptionReminded, ListMaintenance, Dashboard, ListStaffRatings.
  - `IOperationsRepository`: Dashboard, ListStaffRatings.
  - `IUserService`: EmailExists, UpdateAvatar.
  - `IRentalLifecycleRepository`: GetHistory, GetSettlement.
  - `IVehicleAvailabilityService`: AssertAvailable.
  - `IWorkflowStateStore`: Set, Delete.
  - Các stub tương ứng trong tests.
- Chuyển 5 hosted service sang `Infrastructure/HostedServices/` và `NpgsqlLoyaltyRepository` sang `Infrastructure/Repositories/`.
- Thống nhất namespace:
  - `Car_Rental_System_BE.Controllers` → `Car_Rental_System_BE.WebApi.Controllers`.
  - `VehicleRental.Domain.{Lifecycle,Loyalty}` → `Car_Rental_System_BE.Domain.{Lifecycle,Loyalty}`.
- Ngừng track `.dotnet-cli/` (đã thêm vào .gitignore). Xoá `Domain/ValueObjects/README.md` vì nội dung đã sai.
- Thêm `docs/flow.md`, viết `README.md`, cập nhật cây thư mục trong `docs/architecture.md` và mục Infrastructure trong AGENTS.md.

## Chưa làm
Các refactor lớn đã nêu trong review, chưa làm:
- tách `IOperationsService`/`IOperationsRepository` theo aggregate;
- đưa business rule ra khỏi repository SQL;
- dùng enum thay chuỗi status/payment;
- tách DI khỏi `Program.cs`;
- bắt buộc inject `TimeProvider`.

Các endpoint FE không gọi (Devices, Home, Loyalty, Map…) chưa xoá vì cần chủ sản phẩm xác nhận.

## Test
- `dotnet build`: 0 warning, 0 error.
- `dotnet test`: 890 pass, 364 fail. Đúng bằng baseline trước khi sửa, không có test mới fail. Toàn bộ test fail là do môi trường local không kết nối được PostgreSQL (`28P01`).

## Quyết định/giả định
- Không thay đổi hành vi API, route hay schema DB.
