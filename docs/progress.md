# Tiến độ

| ID | Tên | Milestone | Priority | Status | Spec | Change gần nhất |
|---|---|---|---|---|---|---|
| Bootstrap | Docs, architecture, Docker, CI | M0 | P0 | In progress | NFR-001..008 | 2026-10-02-local-migration-path |
| FR-001 | Đăng ký OTP | M2 | P0 | In progress | FR-001 | 2026-09-30-account-basics |
| FR-002 | Verify/resend OTP | M2 | P0 | In progress | FR-002 | 2026-09-30-account-basics |
| FR-003 | Access/refresh token cookies | M2 | P0 | In progress | FR-003 | 2026-10-03-access-refresh-token-cookies |
| FR-004..007 | Account/RBAC | M2 | P0-P1 | In progress | FR-004..007 | 2026-10-03-staff-authorization |
| FR-008..009,011 | Catalog/operations | M3 | P0-P1 | In progress | FR-008..011 | 2026-10-03-vehicle-search-envelope-fixes |
| FR-010 | Tìm xe & availability (BE-10) | M3 | P0 | Done | FR-010 | 2026-10-04-vehicle-availability |
| FR-012..018 | Rentals | M4 | P0-P1 | In progress | FR-012..018 | 2026-10-03-staff-authorization |
| FR-019..021 | Payments | M5 | P1 | In progress | FR-019..021 | 2026-10-03-subscription-plan-payments |
| FR-022..025 | Subscription | M6 | P0-P1 | Done | FR-022..025 | 2026-10-03-subscription-plan-payments |
| FR-026..027,029..030 | Maintenance/report/loyalty/dashboard | M7 | P1-P2 | In progress | FR-026..027,029..030 | 2026-10-03-staff-authorization |
| FR-028 | Brevo notifications | M7 | P0 | In progress | FR-028 | 2026-09-30-bootstrap |
| NFR-001..005,008 | Architecture/platform/security | M0 | P0 | In progress | NFR files | 2026-09-30-bootstrap |
| NFR-006..007 | Performance/testing | M0 | P1 | In progress | NFR files | 2026-10-01-test-project |

## Tổng kết
Done: FR-010 (tìm xe và availability), FR-022..025 (subscription, gồm cả thanh toán mua gói); In progress: các nhóm còn lại; Not started: các nhóm chưa chạm. `dotnet build -warnaserror` đã xanh 0 warning/0 error; test suite **159/159 pass** ngày 2026-10-04.

## Tìm xe và availability (FR-010 / BE-10) — 2026-10-04

Đã triển khai toàn diện module tìm xe và kiểm tra tính khả dụng theo Clean Architecture:
- Migration `0011_vehicle_bookings_and_availability.sql`: ràng buộc `ck_vehicles_status`, bảng `vehicle_bookings` với gist exclusion constraint (`ex_vehicle_bookings_no_overlap`), trigger đồng bộ từ `hour_rental_detail`, `longterm_rental_contract`, `rentals`, và view `v_vehicle_maintenance_blocks`.
- Domain: `TimeRange` value object với đệm đối xứng 60 phút, `AvailabilityReason`, `BookingStatusRules`.
- Application: `IVehicleSearchRepository`, `IVehicleAvailabilityService`, `IDriverAvailability`, `VehicleSearchService`, `AvailabilityOptions` (`ValidateOnStart`), `BusinessRuleException` với extension `code`.
- Infrastructure: `NpgsqlVehicleSearchRepository` với tìm kiếm filter/sort/paging và index gist, `NpgsqlVehicleAvailabilityService` với advisory lock 1001, `NpgsqlDriverAvailability`.
- WebApi: `VehicleSearchController` (5 endpoints công khai và nội bộ), rate limiter `public-search`, ánh xạ message trong `ApiSuccessResponseFilter`.
- Sửa `CreateRentalAsync`: dùng `VehicleLock.AcquireAsync` + `AssertAvailableAsync` và bắt lỗi `23P01`.
- Kiểm thử: 159 tests pass 100% bao gồm schema tests, integration tests, concurrency tests (đua tranh), và API contract tests. Xem [thay đổi](../changes/2026-10-04-vehicle-availability.md) và [ADR-002](decisions/ADR-002-vehicle-bookings-buffer.md).

## Subscription — 2026-10-03

FR-024 (thuê bằng gói, trừ `remaining_hours`, khóa `FOR UPDATE` chống double deduct) và FR-025 (job hết hạn + nhắc trước hạn, UTC, idempotent) đã hoàn thành. `customer_subscriptions` thêm `reminder_sent_at` (migration `0009`). Integration test cho trừ giờ/chống double deduct và cho job hết hạn. Sửa nốt `[property:]` ở `PlanRequest`/`RefundRequest` sang `[param:]`. **BE-21/FR-023 đã hoàn tất phần thanh toán:** mua gói đi qua PayOS checkout + webhook mới tạo subscription, thêm `GET /api/customer-subscriptions` (migration `0010`). Xem [thay đổi mua gói](../changes/2026-10-03-subscription-plan-payments.md) và [thay đổi trừ giờ/hết hạn](../changes/2026-10-03-subscription-rental-expiry.md).

## Sửa lỗi catalog & envelope — 2026-10-03

`GET /api/vehicles` trả 500 vì hai lỗi độc lập: `42P08` khi tham số lọc NULL (repository) và `InvalidCastException` khi response filter wrap envelope nhưng giữ `ObjectResult.DeclaredType` (WebApi). Đã sửa cả hai, thêm `VehicleSearchTests` + test hồi quy cho filter, và sửa test `SubscriptionExpiryTests` vi phạm `ck_cs_dates`. Xem [thay đổi](../changes/2026-10-03-vehicle-search-envelope-fixes.md).

## RBAC — 2026-10-03

Đã triển khai policy theo specialty, staff/department claims, quyền theo chủ rental/assignment, inspection actor và thu hồi quyền theo authorization version. Migration 0008 đã được kiểm tra trên database test độc lập; 69/69 test pass, tất cả project build thành công. Xem [thay đổi](../changes/2026-10-03-staff-authorization.md) và [hợp đồng frontend](staff-authorization.md). CSRF và các acceptance nghiệp vụ chưa liên quan vẫn giữ trạng thái chưa hoàn thành.
