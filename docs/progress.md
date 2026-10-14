# Tiến độ

## Localhost startup — 2026-10-14

Development tự dùng PostgreSQL `localhost:5432` và Redis `localhost:6379` theo Docker Compose nếu chưa có connection string riêng. Production không thay đổi. Để chạy đầy đủ, cần có PostgreSQL/Redis đang hoạt động và cấu hình `Jwt__SigningKey`, `Brevo__ApiKey`, `Brevo__SenderEmail`; Swagger dùng `http://localhost:3000/swagger`. Build WebApi pass (0 warning/error). Chưa xác nhận kết nối thật vì Docker CLI và PostgreSQL/Redis local không khả dụng trong môi trường kiểm tra. Xem [thay đổi](../changes/2026-10-14-localhost-startup-defaults.md).

## Định vị GPS, Tích hợp Google Maps, Lộ trình và Cảnh báo — 2026-10-10

Bổ sung đầy đủ các module tính năng:
1. API nhận tọa độ GPS telemetry (`POST /api/tracking/ingest`), kiểm soát và xác thực thiết bị qua API key hash (`tracking_devices`), cập nhật vị trí tức thời (`vehicle_last_positions`) và lưu trữ lịch sử vị trí (`vehicle_positions`).
2. Chính sách lưu giữ dữ liệu: dọn dẹp dữ liệu vị trí cũ hơn số ngày quy định định kỳ qua `TelemetryRetentionHostedService` và API thủ công (`POST /api/tracking/retention/purge`).
3. Tích hợp Google Maps API: Places (`/api/map/places`), Geocoding & Reverse Geocoding (`/api/map/geocode`, `/api/map/reverse-geocode`), Distance Matrix (`/api/map/distance-matrix`), Directions (`/api/map/directions`) với cơ chế fallback ngoại tuyến/mô phỏng an toàn.
4. Truy vấn xe theo bán kính (`GET /api/tracking/vehicles/nearby`) sử dụng công thức khoảng cách Haversine trên SQL và tính khoảng cách/ETA thực tế qua Google Maps API.
5. Tổng hợp lộ trình chuyến thuê (`GET /api/tracking/rentals/{rentalId}/route`) gồm quãng đường, thời gian, vận tốc trung bình/tối đa và danh sách tọa độ.
6. Quản lý vùng địa lý (`geofences`) và phát hiện chuyển đổi trạng thái ENTER/EXIT (`geofence_events`). Tự động cảnh báo khi vào vùng cấm hoặc rời vùng cho phép (`tracking_alerts`).
7. Cảnh báo xe đi lệch lộ trình (route deviation) dựa trên khoảng cách tới polyline dự kiến và kiểm tra trả sai điểm (`POST /api/tracking/rentals/{rentalId}/verify-dropoff`) đối chiếu với trạm quy định của đơn vị cho thuê.

Toàn bộ API danh sách tuân thủ quy tắc phân trang (`page`, `pageSize`, `items`, `totalCount`) và hợp đồng envelope 6 trường bắt buộc. Thêm migration `0015_vehicle_tracking_and_geofencing.sql`, tài liệu [FR-033](../specs/FR-033-telemetry-and-device-control.md), [FR-034](../specs/FR-034-google-maps-places-and-eta.md), [FR-035](../specs/FR-035-routes-geofencing-and-alerts.md) và change log [2026-10-10-gps-tracking-and-google-maps.md](../changes/2026-10-10-gps-tracking-and-google-maps.md).

## Rule pagination — 2026-10-10

AGENTS.md và Definition of Done bắt buộc mọi API list dùng page=1/pageSize=20, pageSize tối đa 100, data có items/page/pageSize/totalCount, validation tại HTTP và Application. NFR-008/API reference/ADR-003 đã đồng bộ. Đây là thay đổi rule/tài liệu; các API cũ còn trả array chưa được chuyển đổi trong lượt này. Kiểm tra diff/link tài liệu, không chạy lại build/test do không sửa runtime. Xem [change](../changes/2026-10-10-list-pagination-rule.md).

## Customer directory và upload Cloudinary — 2026-10-10

Thêm GET danh sách/chi tiết customer cho Admin/Manager/CustomerService, có phân trang/tìm kiếm/lọc active và profile/license/loyalty an toàn. PUT customer license nhận multipart và lưu secureUrl sau upload, đặt lại pending; JSON URL chuyển sang `/api/customer/license/url`. Bổ sung upload ảnh bằng lái nhân viên, inspection và report theo quyền tạo dữ liệu tương ứng. Avatar/ảnh xe đã có upload. Lỗi Cloudinary trả 500 an toàn, không ghi license khi provider thất bại.

Build warnings-as-errors thành công; **249/249 test pass**, gồm HTTP multipart/Swagger/envelope/auth, Application và PostgreSQL 18.4 riêng. PostgreSQL kiểm thử đã dừng. Cloudinary dùng stub; chưa kiểm chứng upload vào tài khoản thật. Không migration; private delivery/cleanup ảnh cũ và Docker còn chưa kiểm chứng. Xem [change](../changes/2026-10-10-customer-directory-media.md) và [API](API.md).

## Giới hạn mật khẩu — 2026-10-07

Đăng ký/đăng nhập dùng giới hạn chung **6–20 ký tự**, bao gồm hai mốc; tạo nhân viên/đổi/reset mật khẩu mới dùng cùng rule để tránh tạo credential vượt giới hạn login. Lỗi trả 400 với envelope hiện tại và thông báo tiếng Việt. Swagger đọc minLength/maxLength từ record parameters; AGENTS và API reference đã cập nhật. Build 0 warning/error; **110/110** test response/validation/unit/authorization/refresh authorization pass, gồm các mốc 5/6/20/21. Không thay hash/session hoặc migration; không chạy lại DB suite do lỗi xác thực local đã ghi. Account cũ có password >20 cần reset. Xem [change](../changes/2026-10-07-password-length.md).

## WebApi project naming correction — 2026-10-07

Theo xác nhận người dùng, project/folder/assembly giữ tên `VehicleRental.WebApi`.
Namespace HTTP dùng `Car_Rental_System_BE.WebApi`; solution/test references, Docker và source links đã cập nhật.
Build và Release publish thành công, 0 warning/error; 124/124 regression tests pass.
Các thư mục con và nghiệp vụ giữ nguyên. Thư mục `VehicleRental.API` đã xóa hết source/build artifacts
nhưng root rỗng còn bị Windows process khóa. Không còn project/reference ở đó.
Xem [change](../changes/2026-10-07-webapi-project-name.md).

## Reference source layout — 2026-10-07

Sắp xếp theo hình người dùng: Domain (`Entities`, `ValueObjects`, `Services`, `Interfaces`);
Application (`Services`, `Interfaces`, `DTOs`, `Validators`); Infrastructure
(`Persistence`, `Repositories`, `Services`); API (`Controllers`, `Middlewares`).
Request/response DTO tách khỏi Controller, port tách khỏi DTO/implementation, namespace khớp folder.
Project/directory/assembly giữ tên `VehicleRental.WebApi`; solution, Docker, tests và source links cập nhật.

Build warnings-as-errors 0 warning/error; **183/183 test pass** trên PostgreSQL 18.4 độc lập,
server đã dừng. Release publish tạo đúng `VehicleRental.WebApi.dll`. Docker engine chưa chạy nên
chưa kiểm chứng Docker build. Tên project WebApi đã được xác nhận lại ở cập nhật phía trên.
Không migration/thay đổi API contract/nghiệp vụ.
Xem [change](../changes/2026-10-07-reference-layer-layout.md).

## Services/Repositories — 2026-10-07

Thêm Application Services cho account/session/staff/media/payment/operations/user và OTP/reset;
Controller gọi service thay vì repository/Redis implementation. Repository interfaces tách khỏi DTO
sang `Application/Repositories`; các query Npgsql ở `Infrastructure/Repositories`.
Application dùng cache/session/token/email/payment ports, không tham chiếu HTTP/Redis/Npgsql.

Build warnings-as-errors: 0 warning/error. Boundary/use-case/HTTP/auth tests: 124/124 pass;
toàn bộ suite: **183/183 pass** trên PostgreSQL 18.4 test riêng và đã dừng server sau chạy.
Diff whitespace sạch. Docker chưa kiểm chứng do Docker Desktop engine không chạy.
Không có migration, đổi route hay response contract. Operations còn cần tách theo aggregate;
invariant đang đọc dữ liệu dưới database lock vẫn giữ tại repository transaction.
Xem [change](../changes/2026-10-07-services-repositories.md) và [architecture](architecture.md).

## Response contract — 2026-10-07

Quy tắc duy trì contract đã được ghi trong [AGENTS.md](../AGENTS.md), áp dụng cho các lần thêm/sửa API tiếp theo; Definition of Done dùng ProblemDetails trong `error`.

Chuẩn hóa response JSON success/error thành `code`, `status`, `is_success`, `data`, `message`, `error`; code khớp HTTP, status success/error, is_success boolean. MVC always-run filter xử lý automatic validation; middleware bao phủ exception/auth/route; health và Swagger dùng contract tương ứng. Build không warning/error; **97/97** test response HTTP, unit, authorization và refresh authorization pass. Nhóm PostgreSQL bị lỗi xác thực DB local ở lượt lọc ban đầu nên chưa xác nhận lại toàn bộ integration suite. Không migration; không gọi PayOS/Redis/DB triển khai. Xem [change](../changes/2026-10-07-api-response-contract.md) và [ADR-002](decisions/ADR-002-api-response-envelope.md).

## Tài liệu API — 2026-10-07

Thêm [API reference tiếng Việt](API.md) cho 90 controller endpoints, các route dịch vụ và callback OAuth. Có quyền truy cập, cookie/refresh, DTO, mã HTTP sau response filter, quy tắc thuê/thanh toán và ví dụ frontend. Kiểm tra danh sách route với controller, JSON minh họa và link tài liệu nội bộ; chưa gọi hệ thống chạy thật. Chỉ thay đổi tài liệu, không có migration hoặc thay đổi runtime. NFR-008 vẫn In progress. Xem [change](../changes/2026-10-07-api-documentation.md).

## BE-12/BE-13/BE-15 audit — 2026-10-05
Hourly/long-term creation, transactional interval locks and pre-inspection gating already exist. Added signed contract PUT API and exposed unit-price snapshots/contract metadata in rental responses. Build/test with warnings as errors: 131/131 tests pass against isolated PostgreSQL 18. Corrected shared vehicle test seed to include required category/fuel. No migration required; Docker deployment not verified. FR-013 renewal/payment schedules, FR-017 automated overtime and dedicated Domain Rental aggregate remain pending. See [API guide](rental-contract-api.md).

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
| FR-032 | Customer directory list/detail | M2 | P1 | Done | FR-032 | 2026-10-10-customer-directory-media |
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
