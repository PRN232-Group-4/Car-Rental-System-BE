# Tiến độ

| ID | Tên | Milestone | Priority | Status | Spec | Change gần nhất |
|---|---|---|---|---|---|---|
| Bootstrap | Docs, architecture, Docker, CI | M0 | P0 | In progress | NFR-001..008 | 2026-10-02-local-migration-path |
| FR-001 | Đăng ký OTP | M2 | P0 | In progress | FR-001 | 2026-09-30-account-basics |
| FR-002 | Verify/resend OTP | M2 | P0 | In progress | FR-002 | 2026-09-30-account-basics |
| FR-003 | Access/refresh token cookies | M2 | P0 | In progress | FR-003 | 2026-10-03-access-refresh-token-cookies |
| FR-004..007 | Account/RBAC | M2 | P0-P1 | In progress | FR-004..007 | 2026-10-03-staff-authorization |
| FR-008..011 | Catalog/operations | M3 | P0-P1 | In progress | FR-008..011 | 2026-10-03-vehicle-search-envelope-fixes |
| FR-012..018 | Rentals | M4 | P0-P1 | In progress | FR-012..018 | 2026-10-03-staff-authorization |
| FR-019..021 | Payments | M5 | P1 | In progress | FR-019..021 | 2026-10-03-subscription-plan-payments |
| FR-022..025 | Subscription | M6 | P0-P1 | Done | FR-022..025 | 2026-10-03-subscription-plan-payments |
| FR-026..027,029..030 | Maintenance/report/loyalty/dashboard | M7 | P1-P2 | In progress | FR-026..027,029..030 | 2026-10-03-staff-authorization |
| FR-028 | Brevo notifications | M7 | P0 | In progress | FR-028 | 2026-09-30-bootstrap |
| NFR-001..005,008 | Architecture/platform/security | M0 | P0 | In progress | NFR files | 2026-09-30-bootstrap |
| NFR-006..007 | Performance/testing | M0 | P1 | In progress | NFR files | 2026-10-01-test-project |

## Tổng kết
Done: FR-022..025 (subscription, gồm cả thanh toán mua gói); In progress: các nhóm còn lại; Not started: các nhóm chưa chạm. `dotnet build -warnaserror` đã xanh; Docker, hosted CI còn cần xác nhận; test suite **84/84 pass** ngày 2026-10-03.

## Subscription — 2026-10-03

FR-024 (thuê bằng gói, trừ `remaining_hours`, khóa `FOR UPDATE` chống double deduct) và FR-025 (job hết hạn + nhắc trước hạn, UTC, idempotent) đã hoàn thành. `customer_subscriptions` thêm `reminder_sent_at` (migration `0009`). Integration test cho trừ giờ/chống double deduct và cho job hết hạn. Sửa nốt `[property:]` ở `PlanRequest`/`RefundRequest` sang `[param:]`. **BE-21/FR-023 đã hoàn tất phần thanh toán:** mua gói đi qua PayOS checkout + webhook mới tạo subscription, thêm `GET /api/customer-subscriptions` (migration `0010`). Xem [thay đổi mua gói](../changes/2026-10-03-subscription-plan-payments.md) và [thay đổi trừ giờ/hết hạn](../changes/2026-10-03-subscription-rental-expiry.md).

## Sửa lỗi catalog & envelope — 2026-10-03

`GET /api/vehicles` trả 500 vì hai lỗi độc lập: `42P08` khi tham số lọc NULL (repository) và `InvalidCastException` khi response filter wrap envelope nhưng giữ `ObjectResult.DeclaredType` (WebApi). Đã sửa cả hai, thêm `VehicleSearchTests` + test hồi quy cho filter, và sửa test `SubscriptionExpiryTests` vi phạm `ck_cs_dates`. Xem [thay đổi](../changes/2026-10-03-vehicle-search-envelope-fixes.md).

## RBAC — 2026-10-03

Đã triển khai policy theo specialty, staff/department claims, quyền theo chủ rental/assignment, inspection actor và thu hồi quyền theo authorization version. Migration 0008 đã được kiểm tra trên database test độc lập; 69/69 test pass, tất cả project build thành công. Xem [thay đổi](../changes/2026-10-03-staff-authorization.md) và [hợp đồng frontend](staff-authorization.md). CSRF và các acceptance nghiệp vụ chưa liên quan vẫn giữ trạng thái chưa hoàn thành.

## O2/O4/O5/BE-24 — 2026-10-04
FR-031: licensePlate, ca trực, report staff/driver, Manager giao Maintenance, inspection độc lập tài chính, đổi xe đúng số chỗ/đổi tài khi renting và giữ giá. Migration 0011; PostgreSQL test độc lập. Customer report và tính phí bồi thường tự động chưa triển khai. Xem docs/operational-recovery.md.

Kiểm chứng 2026-10-04: build -warnaserror thành công, 101/101 tests pass trên PostgreSQL 18 tạm; chưa chạy migration trên Neon, chưa kiểm chứng Docker deployment.

## Department assignment — 2026-10-05
FR-007/FR-008: Admin assigns Manager/Staff to departments; Manager assigns Staff only. Dedicated user-ID endpoint preserves specialties, protects multi-role Manager/Admin targets, validates references, and revokes credentials. Manager can read staff/departments; personnel CRUD remains Admin-only. All 112 tests pass against isolated PostgreSQL 18. No new migration. See [change](../changes/2026-10-05-department-assignment.md).

## Facility assignment — 2026-10-05
FR-007/FR-008: Admin assigns Staff/Manager to facilities; Manager assigns Staff only. Department and other employee fields are preserved. Shared organization-assignment logic validates references and protected roles, then invalidates credentials. Verification: 116/116 tests pass with isolated PostgreSQL 18; no application database changes or new migration. See [change](../changes/2026-10-05-facility-assignment.md).
