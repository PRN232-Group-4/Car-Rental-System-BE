# Tiến độ

## Phase 2 quyết toán thuê một transaction và hoá đơn supplementary, 2026-11-05

Phase 2 gồm `9b2e5b4` (quyết toán hoàn tất trong một transaction, trừ giờ gói đúng một lần, migration 0033), `e56372b` (loyalty đọc trên connection của transaction), `ce8f7c4` (hoá đơn supplementary thu được bằng cash và PayOS, migration 0034), `7eb105a` (VAT trên hoá đơn supplementary) và `356b6c7` (route trạng thái cũ gọi lifecycle service). Writer báo build 0 warning và toàn suite 1.299 đạt sau commit cuối; reviewer độc lập (Claude Code, model `claude-opus-5-5`, so với base `main` tại `e438f7f`) chạy toàn suite Neon 1.422 đạt tại `248de35` và kết luận không có khiếm khuyết nào buộc phải sửa code, chỉ còn rủi ro tồn đọng ghi ở mục Giới hạn của tài liệu thay đổi: kiểm tra hoàn tiền theo rental của `RentalRefundWriter`, rental gói thiếu `customer_subscription_id`, và hai thay đổi hành vi ở đường loyalty và route trạng thái lỗi thời. Message `7eb105a` hướng dẫn chạy backfill VAT trước khi phát hành; hướng dẫn đó sai và đã được đính chính: production không cần backfill VAT, chỉ hoá đơn `kind='supplementary'` trong database dev/test đã chạy `ce8f7c4` trước `7eb105a` mới cần sửa. Hai CHECK của migration 0034 mới chỉ validate trên DB rỗng và DB seed demo, nên dòng production vi phạm sẽ làm migration fail. Chưa push/deploy. Xem [thay đổi](../changes/2026-11-05-phase2-rental-settlement.md).

## Tách slice Billing, xác minh Handovers và Inspections, tách Dispatch và Maintenance, 2026-11-05

Billing đã tách tại `c031bb1`, giữ API và logic tài chính. Lead kiểm build 0 warning/error và 81 test contract đạt; worker báo toàn suite Neon 1.380 test đạt. Handovers và Inspections đã có slice riêng từ trước nên 3.2 và 3.3 chỉ xác minh; Lead chạy 175 và 87 test trên Neon, tất cả đạt. Dispatch tại `44a1f93`: ca làm tài xế chuyển sang slice Assignments với SQL giữ nguyên từng byte, và route lỗi thời `POST /api/rental/{id}/assignments` bị xóa theo yêu cầu, nên đây là thay đổi API không tương thích ngược; cổng kiểm tra ca trực chỉ route cũ có nay mất và được ghi thành nợ kỹ thuật. Maintenance tại `3a4e730`: slice riêng, ba route giữ nguyên, 77 dòng SQL giữ nguyên từng byte.

Reviewer độc lập chấp nhận cả năm bước; lượt 3.3, 3.4 và 3.5 dùng `river/zai-org/GLM-5.3-Flash` vì GPT-6 Astra hết hạn mức, nên đa dạng model bị giảm. Toàn bộ test DB chạy trên database dùng một lần. Chưa push/deploy; Phase 3 không còn hạng mục code. Giới hạn coverage và việc D2/FE còn lại được ghi trong [thay đổi](../changes/2026-11-05-slice-extraction.md).

## Cookie SameSite cho web cross-site — 2026-10-29

`AuthCookies.Write`/`Clear` chọn `SameSite=None` khi request có header `Origin` (browser gọi API khác domain: Flutter web `http://localhost:5000` → Railway, FE Cloudflare Workers → API) và giữ `SameSite=Lax` cho native/server-to-server; `HttpOnly`/`Secure` không đổi, `Clear` dùng cùng `SameSite` với `Write`. Sửa lỗi login trả 200 nhưng `GET /api/account/profile` trả 401 vì trình duyệt chặn `Set-Cookie` `SameSite=Lax` từ response cross-site. Chưa build/chạy test trong phiên này; chưa deploy. Xem [thay đổi](../changes/2026-10-29-cookie-samesite-cross-site.md).

## CORS frontend — 2026-10-28

Thêm CORS cho FE Cloudflare Workers và HTTP localhost cổng 3000/5000 trong mọi môi trường; cho phép method/header và credentials, preflight trước rate limiting/xác thực. Build đạt 0 warning/error; 34/34 `ApiResponseTests` hiện có đạt. Xem [thay đổi](../changes/2026-10-28-frontend-cors.md). Chưa deploy; cần triển khai lại BE.

## Gộp Customer detail và tên API số ít/số nhiều — 2026-10-25

Gộp hai partial detail còn lại của Customer vào controller/service chính; `src` giảm 236 → 234 file C#. API tập hợp giữ số nhiều, tài nguyên có ID dùng số ít; đồng bộ route, Location, Swagger metadata, FE adapters/schema và mock e2e. URL hợp đồng cũ đã lưu được chuẩn hóa khi đọc qua cả EF/ADO, không migration. Rà 179 controller actions không trùng route. Build warnings-as-errors đạt; toàn suite BE **1.254/1.254 pass, 0 fail/skip** trên PostgreSQL 18 tạm UTC; FE typecheck và **97/97 test liên quan** đạt. URL đổi không tương thích ngược, cần phát hành BE/FE đồng bộ. Xem [thay đổi](../changes/2026-10-25-file-consolidation-api-number.md), [ADR-013](decisions/ADR-013-api-resource-number.md). Chưa deploy.

## Entity Framework và gộp file — 2026-10-25

Truy vấn đọc thông thường dùng LINQ/EF Core ở Infrastructure, mapping 46 bảng/3 view hiện có. Giữ SQL cho ghi, khóa và kiểm tra nội bộ trong transaction; không migration/deploy. Gộp 12 file nhỏ, tổng source `src` 246 → 236. Build solution và publish Release đạt 0 warning/error. Toàn suite **1.248/1.248 pass, 0 fail/skip** sau sửa 12 fixture/test lỗi cũ và thêm 3 ca kiểm tra từ chối quyền upload. Không nới điều kiện nghiệp vụ. Docker Desktop chưa mở được Linux engine dù đã thử khởi động, nên container chưa kiểm chứng. Xem [thay đổi](../changes/2026-10-25-entity-framework-read-queries.md), [ADR-012](decisions/ADR-012-entity-framework-read-queries.md) và [báo cáo gộp file](file-consolidation-review.md).

## Tự động hết hạn yêu cầu đặt xe — 2026-10-23

Thêm trạng thái `expired`, cột `expire_at`/`expired_at` (migration 0032) và job quét mỗi phút: yêu cầu chưa được duyệt hoặc chưa thanh toán trong cửa sổ cấu hình (mặc định 1 giờ kể từ lúc đặt) tự hết hạn, hủy hóa đơn chưa thu và giải phóng lịch xe. Đặt xe phải cách giờ nhận tối thiểu 1 giờ; đơn hết hạn không duyệt/không thanh toán được, thanh toán muộn sau hạn được ghi ledger và tự tạo yêu cầu hoàn tiền. 32 test FR-042 đạt trên PostgreSQL tạm; toàn bộ suite 1144 đạt, 3 lỗi có sẵn từ trước. Chưa deploy, chưa gửi thông báo email. Xem [FR-042](../specs/FR-042-rental-expiry.md), [thay đổi](../changes/2026-10-23-rental-expiry.md).

## Ẩn xe chưa cấu hình hạng GPLX khỏi danh mục khách — 2026-10-23

Public search yêu cầu ít nhất một hạng GPLX khác khoảng trắng trước đếm tổng/phân trang; landing, tìm xe và booking dùng cùng API. Giữ xe trong quản trị, chi tiết và lịch sử; GPS nearby vận hành không đổi. Build tests `-warnaserror` đạt 0 lỗi/0 cảnh báo; 77/77 ca search/catalog/Application/HTTP/Swagger/inspection liên quan đạt trên PostgreSQL test riêng. Fixture subscription self-drive được cập nhật sang GPLX approved theo invariant đã có. Không migration/deploy; WebApi local đang chạy cần nạp lại bản build để áp dụng filter mới. Xem [thay đổi](../changes/2026-10-23-hide-unconfigured-vehicles.md).

## Nhiều GPLX và hạng bằng theo xe — 2026-10-23

Đã lưu/duyệt độc lập nhiều GPLX, cấu hình allowedLicenseTypes, kiểm tra phù hợp ở booking/edit/assignment/handover. Migration 0030 backfill hồ sơ cũ. Build đạt 0 lỗi/0 cảnh báo; 133 ca liên quan đạt trên PostgreSQL riêng. Chưa áp dụng migration DB ứng dụng; xem [thay đổi](../changes/2026-10-23-multiple-customer-licenses.md).

## Bàn giao đơn tự lái không cần phân tài xế — 2026-10-22

Đơn tự lái đã chọn xe được tự ghi nhận assignment không có tài xế trong transaction bàn giao sau kiểm tra xe hiện tại, kiểm định, GPLX, thanh toán và lịch trống. Không tạo trùng khi bàn giao đồng thời; chặn phân công sai xe hoặc có tài xế trên đơn tự lái. Đơn có tài xế vẫn phải phân tài xế thực tế. Build `-warnaserror` đạt, 63/63 kiểm thử lifecycle/handover/inspection đạt trên PostgreSQL riêng với session UTC. Không migration hoặc deploy; cần nạp lại backend để dùng bản sửa. Xem [thay đổi](../changes/2026-10-22-self-drive-handover.md).

## Đồng bộ tiến trình khi kiểm tra kèm duyệt — 2026-10-22

Checked hỗ trợ approveRequest=true theo xác nhận rõ của Admin để duyệt pending trong cùng transaction với evidence. Dùng writer chung với lifecycle confirm; không tự start/paid hoặc duyệt driver_default. 58 backend tests liên quan đạt, không migration mới/chưa deploy. Xem [thay đổi](../changes/2026-10-22-inspection-approval-progress.md).

## Quá hạn và thu phí sau trả xe — 2026-10-22

Đã thêm bộ lọc overdue trước phân trang/isOverdue, giữ lifecycle để nhận trả xe. Thu cash hoặc tạo checkout PayOS cho hóa đơn thiếu liên kết settlement completed; có audit/idempotency và lock chống thu trùng, không phát hành order từ overtime. 109 backend tests liên quan đạt trên PostgreSQL riêng; Build Release đạt. Xem [FR-037](../specs/FR-037-overdue-collection.md), [thay đổi](../changes/2026-10-22-overdue-collection.md). Chưa deploy/không thêm migration mới.

## Customer hủy trước Đang thuê và phiên bản inspect Railway — 2026-10-22

Customer chủ đơn được hủy pending/pre_inspection, kể cả nhận trễ chưa renting; giữ phí/hoàn hiện có. Mine canCancel, preview và cancel đồng bộ; 148 backend tests liên quan đạt trên PostgreSQL riêng. Đã xác nhận Swagger Railway có inspection-assignment nhưng chưa có checked, nên Not Found hiện tại cần triển khai backend/migration 0028 từ source mới. Chưa deploy/migrate database ứng dụng. Xem [thay đổi](../changes/2026-10-22-customer-cancel-before-renting.md).

## Xe customer và kiểm tra theo hình thức lái — 2026-10-22

Đã lọc xe đang thuê khỏi public search/kiểm tra lại khi tạo đơn, lưu hasDriver cho cả hourly/longterm, tự xác nhận kiểm tra trước cho đơn có tài xế, và thêm Admin POST inspection-assignment/checked nguyên tử. Directory có nguồn/người xác nhận; migration 0028 backfill đơn có tài xế chưa bắt đầu, không ghi đè kiểm tra thật. 244/244 tests liên quan đạt trên PostgreSQL riêng, gồm HTTP/Swagger. Chưa deploy/migrate database ứng dụng. Xem [thay đổi](../changes/2026-10-22-customer-availability-inspection-modes.md).

## Danh sách Admin mới nhất trước — 2026-10-22

Đồng bộ SQL xe theo created_at DESC/id DESC; customer/GPLX, cơ sở, vai trò và nhân viên theo ID DESC trước phân trang. FE sắp đầy đủ danh mục legacy và đổi mặc định nhân viên sang ID giảm dần. 74/74 backend tests liên quan đạt, gồm PostgreSQL riêng và HTTP; không migration/deploy. Xem [thay đổi](../changes/2026-10-22-admin-newest-first.md).

## Loại thuê giờ/dài hạn trong danh sách vận hành — 2026-10-22

GET `/api/rentals` và summary bàn giao trả thêm `rentalType` từ DB, khớp chi tiết; giữ bộ lọc/quyền/pagination. 41/41 kiểm thử HTTP/Swagger/SQL liên quan đạt, 0 skipped; build Release thành công, PostgreSQL 18 riêng với database fixture tạm. Không migration/deploy; cần khởi động lại backend đang chạy. Xem [thay đổi](../changes/2026-10-22-operational-rental-type.md).

## Timeout PostgreSQL metadata và proxy local — 2026-10-22

Thêm cờ `Database:SkipTypeLoading` (mặc định false) cho workaround Npgsql `LoadBackendTypes`; Development local đã bật bằng cấu hình không chứa secret. Build solution `--no-restore -warnaserror` đạt, 0 warning/error. Kiểm chứng chỉ đọc với Npgsql 8.0.5: kết nối Neon 0,53 giây; đọc được text/varchar/int arrays, timestamps, jsonb, numeric, boolean và tsrange. Database hiện có migration `0027_role_catalogue`; lượt kiểm chứng không thực hiện migration.

Đối chiếu timeout đăng nhập: API local `6789` healthy, nhưng manifest proxy Next.js cũ vẫn trỏ Railway. Nạp lại cấu hình Next dev đã đổi `/api/*` sang `http://localhost:6789/api/*`; POST đăng nhập rỗng qua frontend trả validation 400 trong 0,13 giây. Chưa thử thông tin đăng nhập thật. Xem [change](../changes/2026-10-22-database-type-loading-and-local-proxy.md).

## Audit logs và CRUD danh mục role — 2026-10-21

Đã thêm `GET /api/audit-logs` cho Admin/Manager với search actor/target, action và phân trang; chuẩn hóa action cũ, trả metadata thật và ghi IP cho mutation quản trị tài khoản mới. Thêm CRUD `/api/roles` cho Admin, tên duy nhất không phân biệt hoa thường, bảo vệ role hệ thống và role đang gán; custom role dùng được qua API gán/gỡ/lọc users, giữ kiểm tra hồ sơ/chuyên môn, Admin cuối cùng và thu hồi phiên. Swagger và tài liệu đã cập nhật.

Kiểm chứng: build solution `-warnaserror` đạt 0 warning/error; **197/197 test liên quan đạt, không skip**, gồm HTTP/Swagger/envelope, Application, PostgreSQL tạm, ghi IP và concurrency gán/xóa role. Database tạm đã dừng. Chưa deploy hoặc chạy migration lên database ứng dụng; cần áp dụng `0026_audit_log_metadata.sql` và `0027_role_catalogue.sql`. Xem [change](../changes/2026-10-21-audit-logs-role-crud.md), [API](API.md), [FR-041](../specs/FR-041-role-catalog.md).

## Bộ lọc danh mục thuê xe và menu đổi mật khẩu — 2026-10-21

`/find` chuyển sang API search công khai, gửi đúng tiêu chí/ngày giờ UTC, chuẩn hóa URL và ánh xạ nested search DTO lên thẻ xe. Backend thêm phân khúc, sắp xếp tên, tìm tên xe đầy đủ/năm/chi nhánh, tương thích mã hộp số/nhiên liệu với dữ liệu cũ. Menu tài khoản desktop/mobile/customer/admin/driver có lối tắt tới form đổi mật khẩu OTP hiện có. Kiểm chứng: 54/54 test backend Application/HTTP/PostgreSQL, 662/662 unit test frontend và 12/12 E2E Edge desktop/mobile đạt; TypeScript, ESLint các file thay đổi, design lint và production static build đạt. Ba E2E bộ lọc được chạy lại sau đồng bộ giờ picker từ URL và đều đạt. Chưa deploy, cần cập nhật cả FE/BE. Xem [change](../changes/2026-10-21-catalog-filters-account-menu.md).

## Bắt buộc đầy đủ thông tin GPLX khách hàng — 2026-10-21

Hai form `/driver-license` và `/profile` bắt buộc số GPLX, hạng, ngày cấp, ngày hết hạn và ảnh tải lên; lỗi hiển thị dưới từng trường. `/profile` gửi multipart tới API GPLX, giới hạn ảnh 5 MiB. Backend kiểm tra ngày cấp và ảnh ở HTTP/Application, JSON URL cũng không còn cho thiếu ảnh/ngày. Kiểm chứng: 38/38 test backend GPLX, 24/24 test frontend GPLX/inline-validation đạt; TypeScript và ESLint các file thay đổi đạt, không cảnh báo. Chưa deploy hoặc thử Cloudinary thật. Xem [change](../changes/2026-10-21-required-license-fields.md).

## Giới hạn xác thực, badge menu và email nghiệp vụ — 2026-10-21

Đã bổ sung giới hạn mặc định 5 request/60 giây/IP/instance cho đăng ký/đăng nhập/reset; gửi OTP và xác nhận đổi mật khẩu dùng chung ngân sách 5. Redis chặn login/currentPassword sai tối đa 5 lần/10 phút theo email chuẩn hóa; OTP khóa ngay lần sai thứ 5, tăng số lần sai và tiêu thụ mã nguyên tử. Thêm API snapshot năm queue cho dot/số trên menu, giữ scope báo cáo theo staff_id của người gọi và quyền từng nghiệp vụ. Email GPLX/hoàn tiền/trạng thái thuê/kiểm tra xe/báo cáo gửi sau commit, lỗi lookup/provider/timeout không làm thao tác nghiệp vụ trả lỗi.

Kiểm chứng: build với `-warnaserror` và 154/154 test auth/contract/architecture/navigation/email đạt (gồm PostgreSQL tạm/HTTP thật, provider giả); sau sửa assertion validation để await lời gọi bất đồng bộ, 16/16 test xác nhận đạt. Lượt hồi quy mở rộng có 287/291 đạt trước sửa assertion; còn ba lỗi cũ đã được ghi nhận ngày 18/10: hai RentalLifecycleIntegrationTests về overtime/shortfall và RentalLifecycleSchemaTests về SQLSTATE lịch sử bất biến. Không thay đổi tính phí hoặc schema trong lượt này. FE đạt 636 unit test, 8 E2E Edge desktop/mobile sáng/tối, typecheck, design lint và production static build; ESLint 0 lỗi/25 cảnh báo có sẵn.

Chưa kiểm chứng Redis/Brevo thật hoặc deploy. Giới hạn request/IP hiện theo từng instance, counter mật khẩu dùng Redis; email theo best effort không có outbox/retry bảo đảm giao thư. Xem [auth](../changes/2026-10-21-authentication-attempt-limits.md), [navigation](../changes/2026-10-21-navigation-queue-counts.md), [email](../changes/2026-10-21-best-effort-operational-emails.md).

## Đổi mật khẩu có OTP — 2026-10-21

Đã bổ sung gửi OTP cho tài khoản đang đăng nhập và bắt buộc OTP trước khi đổi mật khẩu, dùng workflow/key riêng, TTL/cooldown/giới hạn sai và Redis compare-and-exchange. Mã gắn với mật khẩu hiện tại, email từ claim; sau đổi thu hồi mọi phiên. FE có form dùng chung ở hồ sơ customer/admin/staff. Build backend không warning/error, 82/82 test liên quan đạt; FE 630 unit test, 2 E2E desktop/mobile và build static đạt. Chưa kiểm tra Redis/Brevo thật hoặc Docker (daemon chưa chạy), chưa deploy. Xem [thay đổi](../changes/2026-10-21-password-change-otp.md).

## Email nhân sự và cơ sở tài khoản — 2026-10-21

Đã thêm hồ sơ nhân viên từ tài khoản có sẵn, lấy Driver từ role thực tế, giữ identity và session invalidation. Hồ sơ tài khoản trả facilityName cho mọi role; phân công kiểm tra/bảo trì trả email nhân viên và tên cơ sở. Build -warnaserror đạt, 76/76 test PostgreSQL/HTTP liên quan đạt không skip, gồm role, validation, envelope, Swagger, account lock và dữ liệu thật. Không thêm migration ngoài 0025 trước đó; chưa deploy/Docker. Xem [thay đổi](../changes/2026-10-21-staff-account-facility-identity.md).

## Hoàn thiện API/field cho frontend — 2026-10-20

Đã bổ sung API quản trị users (directory/detail/status/roles/revoke), kiểm duyệt/phản hồi ratings, trạng thái và chi tiết reports, dashboard typed với bốn chuỗi phân tích/so sánh kỳ trước/bộ lọc cơ sở-loại xe. Bổ sung metadata staff, customer rentals, vehicles, policies, maintenance, driver assignment/schedule và hotline cơ sở; các danh sách được sửa có pagination theo ADR-003. Migration mới 0022/0023/0024; dữ liệu chưa có không được điền giả.

Kiểm chứng: build solution với `-warnaserror` đạt; **325/325 test liên quan đạt** trên HTTP localhost và PostgreSQL18 tạm, gồm Swagger/envelope/auth/validation, SQL/projection, migration, totals/empty pages và concurrency. Test runner dùng cấu hình giới hạn song song để phù hợp max_connections của DB tạm. Cập nhật các test cũ cho quy tắc duyệt trước thanh toán FR-036; giữ nguyên kiểm tra an toàn bàn giao. Không chạy toàn bộ test suite hoặc Docker trong đợt này, chưa deploy/chạy migration lên database ứng dụng.

Contract FE cần đồng bộ: rental detail.facility từ string sang object nullable; reports/ratings/maintenance/driver schedule/facilities trả page object. Giới hạn dữ liệu lịch sử dashboard và semantics subscription usage được mô tả trong API/spec. Xem [change](../changes/2026-10-20-frontend-api-completion.md), [API](API.md), [FR-037](../specs/FR-037-user-administration.md), [FR-038](../specs/FR-038-operations-api-completion.md), [FR-039](../specs/FR-039-frontend-api-completion.md).


## Mapbox thay Google Maps — 2026-10-20

Đã thay provider FE/BE bằng Mapbox, giữ quyền/route API và GPS thật; `/places` dùng pagination trên tối đa10 address matches. Bỏ fallback tọa độ/ETA giả, thêm lỗi503/502/404 và422 khi thiếu vị trí cơ sở. Build backend -warnaserror đạt, 84 tests HTTP/provider/tracking/response đạt; FE 512 unit tests, typecheck/ESLint/design lint và static export đạt. 6 E2E đạt (provider mock, gồm vẽ tuyến/ETA, đổi chi nhánh và các panel Admin). Chưa cấp token thật và chưa deploy; xem [setup](mapbox-setup.md) và [change](../changes/2026-10-20-mapbox-migration.md).

## Danh sách đơn Admin và mã đơn trên chi tiết theo dõi — 2026-10-18

Tái sử dụng API rental list/detail hiện có cho mọi trạng thái, giữ phân trang 1/20 tối đa100 và tổng sau lọc. Bổ sung orderId nullable vào summary/chi tiết customer/vận hành, tách mã yêu cầu khỏi đơn chính thức; timeline/audit và timestamps có sẵn phục vụ thanh theo dõi. Build -warnaserror và 44/44 test PostgreSQL/HTTP/Application liên quan đạt; chưa deploy. Xem [change](../changes/2026-10-18-admin-rental-tracking.md).

FE đã có bảng Admin/cột xem và trang Đơn thuê của tôi cho customer, dùng thanh tiến trình xanh 5 bước. Đạt 141 unit FE, 20 E2E liên quan qua các lượt, typecheck/ESLint/design lint. Production build FE mới chưa được chạy do automatic approval review gặp giới hạn sử dụng; không tuyên bố build FE mới đạt.

## Yêu cầu đặt xe và đơn thuê đã thanh toán — 2026-10-18

Đã tách rental request và rental_orders theo xác nhận người dùng; duyệt trước thanh toán, PayOS hoặc staff xác nhận thực nhận tiền mặt mới phát hành order nguyên tử. Gói trả trước phát hành lúc duyệt; bàn giao trực tiếp vẫn cần paid. Invoice detail/Swagger/UI và migration 0021 cập nhật. Build -warnaserror, 91 test backend chọn lọc, 126 unit FE và 24 E2E đạt; typecheck/lint/design/build FE đạt. Kiểm tra mở rộng còn 3 lỗi về SQLSTATE immutable và overtime/shortfall, ghi rõ tại [change](../changes/2026-10-18-booking-request-paid-order.md). Chưa deploy hoặc sửa dữ liệu thật. Xem [ADR-008](decisions/ADR-008-booking-request-paid-order.md).

## Đặt xe theo cơ sở và email OTP — 2026-10-18

Tìm xe có tài xế lọc tài xế rảnh theo cơ sở và khoảng đệm, xử lý cả assignment cũ; search pagination theo rule 1/20, offset Int64 và tổng đúng trên trang rỗng. Email OTP HTML responsive dùng phong cách Thong Dong, có text fallback/escape và dùng chung đăng ký/reset. 55 test liên quan đạt với PostgreSQL18 tạm và HTTP giả; build `-warnaserror` đạt. Chưa gửi email thật/deploy. Xem [change](../changes/2026-10-18-facility-booking-otp-email.md).

## Lọc khách hàng theo GPLX — 2026-10-18

`GET /api/customers` hỗ trợ lọc GPLX trước phân trang và tổng; trạng thái `none` phân biệt hồ sơ trống với hồ sơ đã gửi chờ duyệt. Có validation HTTP/Application, Swagger và API docs. Build `-warnaserror` đạt và 35/35 test CustomerList đạt, gồm PostgreSQL18 tạm và HTTP thực tế. Chưa deploy. Xem [change](../changes/2026-10-18-customer-license-filter.md).

## Sửa đơn trước thuê và bàn giao xe — 2026-10-16

Thêm PUT rental theo ID chỉ trước start, kiểm tra cam kết thanh toán/inspection/assignment/hợp đồng; đồng bộ booking và invoice unpaid nguyên tử. Thêm danh sách/chi tiết/xác nhận handover dùng rental lifecycle, sửa kiểm định sau confirm/return và kiểm tra lại điều kiện start trong transaction. Rental lists validate pagination ở HTTP/Application, offset Int64; sửa truy vấn chi tiết theo schema, response envelope và success messages. Checkout reservation và migration0020 ngăn đổi giá sau phát hành link (invoice unpaid cũ được khóa sửa an toàn khi nâng cấp). **236/236 test chọn lọc đạt**, gồm HTTP/Domain/Application/architecture và PostgreSQL18 tạm; build với -warnaserror đạt. Chưa kiểm chứng PostgreSQL16/PayOS thật, chưa sửa FE hoặc deploy. Xem [change](../changes/2026-10-16-rental-editing-handover.md) và [ADR-007](decisions/ADR-007-rental-editing-and-handover.md).

## Kiểm tra loại xe và thống kê chính sách — 2026-10-15

POST xác minh loại xe ở Application và dưới row lock/transaction khi INSERT; chặn tạo khi cùng phạm vi đã có policy active theo xác nhận người dùng. Kích hoạt lại cũng bị chặn khi có bản active khác; scope lock bảo vệ request đồng thời. GET policy đổi sang pagination 1/20 (max100), thêm rentalCount theo policy_id gồm mọi trạng thái, giữ count khi đổi status; migration 0019 thêm index. FE có cột Số lần thuê, phân trang và lỗi inline. Build/test `-warnaserror` và 147 test HTTP/phân quyền/contract đạt; 122 unit FE và 9 E2E đạt. Test PostgreSQL về count/cạnh tranh đã thêm nhưng chưa kiểm chứng do chưa có TEST_DATABASE_ADMIN hợp lệ. Chưa deploy hoặc sửa dữ liệu thật. Xem change 2026-10-15-rental-policy-validation-usage.

## Đổi trạng thái chính sách thuê xe — 2026-10-15

Thêm `PATCH /api/rental-policies/{id}/status` cho Admin/Manager, validate ID dương và boolean bắt buộc, cập nhật nguyên tử riêng `is_active`, trả bản ghi và envelope chuẩn. Điều khoản và giá rental/invoice cũ giữ nguyên. FE có icon kích hoạt/tạm ngưng, xác nhận và khóa gửi lặp. Đã cập nhật FR-011, ADR-006, Swagger và API docs. Build khi chạy test với `-warnaserror` đạt; 126 test HTTP/validation/phân quyền/contract liên quan đạt. Test persistence đã bổ sung nhưng chưa kiểm chứng PostgreSQL: thông tin kết nối test mặc định bị từ chối xác thực; chưa có `TEST_DATABASE_ADMIN` hợp lệ. Xem change `2026-10-15-rental-policy-status.md`.

## Google Maps frontend — 2026-10-15

FE đã tích hợp Maps Embed tại cơ sở/điểm nhận, preview địa chỉ Admin và giao diện đọc GPS xe/hành trình từ API hiện có. Cập nhật [hướng dẫn cấu hình](google-maps-setup.md); key trình duyệt đặt ở môi trường build FE, tách key backend. Chưa cấu hình/kiểm chứng provider thật hoặc deploy Railway. Backend không thay đổi.

## Sửa đăng ký startup availability và rate limiter — 2026-10-15

Khôi phục bind/validation section `Availability` và đăng ký concrete options cho constructor của `NpgsqlVehicleAvailabilityService`/`VehicleSearchService`. Bổ sung `AddRateLimiter` với policy `public-search` 60 request/phút/IP theo FR-010; response 429 dùng envelope chung. Build solution `--no-restore -warnaserror` đạt, 0 warning/error. Nhóm availability/search/assignment có 44/44 test đạt; kiểm tra mở rộng có 88/93 đạt, 5 test message mapping `VehicleSearch.*` thất bại do mapping thiếu sẵn có, ngoài phạm vi startup. Smoke test dùng endpoint localhost giả và tắt migration đã qua DI/middleware, tới kết nối Redis; chưa kiểm chứng HTTP với PostgreSQL/Redis thật hoặc response 429. Xem [change](../changes/2026-10-15-startup-availability-rate-limiter.md).

## Kiểm tra Google Maps và cấu hình Railway — 2026-10-15

Đã đối chiếu adapter, DI, controller, options, hai appsettings và frontend cùng dự án. Backend có Maps/Tracking API nhưng mặc định tắt, trả fallback khi chưa cấp cấu hình ngoài; frontend chưa nối Maps. Bổ sung [hướng dẫn](google-maps-setup.md) với section appsettings, biến Railway `GoogleMaps__Enabled`/`GoogleMaps__ApiKey`, API Legacy, key restrictions và cách phân biệt dữ liệu Google/fallback. Chưa xác nhận triển khai thật hoặc gọi API tính phí; không đổi runtime/config production. Kiểm tra tài liệu với nguồn chính thức Google/Railway/Microsoft; không chạy lại build/test vì chỉ sửa tài liệu. Xem [change](../changes/2026-10-15-google-maps-configuration-audit.md).

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

Toàn bộ API danh sách tuân thủ quy tắc phân trang (`page`, `pageSize`, `items`, `totalCount`) và hợp đồng envelope 6 trường bắt buộc. Thêm migration `0015_vehicle_tracking_and_geofencing.sql`, tài liệu [FR-033](../specs/FR-033-telemetry-and-device-control.md), [FR-034](../specs/FR-034-mapbox-places-and-eta.md), [FR-035](../specs/FR-035-routes-geofencing-and-alerts.md) và change log [2026-10-10-gps-tracking-and-google-maps.md](../changes/2026-10-10-gps-tracking-and-google-maps.md).

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
| FR-008..009,011 | Catalog/operations | M3 | P0-P1 | In progress | FR-008..011 | 2026-10-03-vehicle-search-envelope-fixes |
| FR-010 | Tìm xe & availability (BE-10) | M3 | P0 | Done | FR-010 | 2026-10-04-vehicle-availability |
| FR-014 | Phân xe/tài xế & Đổi xe (BE-14) | M4 | P0 | Done | FR-014 | 2026-10-04-assignment-vehicle-swap |
| FR-018 | Vòng đời đơn thuê, Quá giờ & Quyết toán (BE-17) | M4 | P0 | Done | FR-018 | 2026-10-06-rental-lifecycle |
| FR-012,013,015..017 | Rentals | M4 | P0-P1 | In progress | FR-012..017 | 2026-10-06-rental-lifecycle |
| FR-019..021 | Payments | M5 | P1 | In progress | FR-019..021 | 2026-10-03-subscription-plan-payments |
| FR-022..025 | Subscription | M6 | P0-P1 | Done | FR-022..025 | 2026-10-03-subscription-plan-payments |
| FR-026..027,029..030 | Maintenance/report/loyalty/dashboard | M7 | P1-P2 | In progress | FR-026..027,029..030 | 2026-10-03-staff-authorization |
| FR-028 | Brevo notifications | M7 | P0 | In progress | FR-028 | 2026-09-30-bootstrap |
| FR-032 | Customer directory list/detail | M2 | P1 | Done | FR-032 | 2026-10-10-customer-directory-media |
| NFR-001..005,008 | Architecture/platform/security | M0 | P0 | In progress | NFR files | 2026-09-30-bootstrap |
| NFR-006..007 | Performance/testing | M0 | P1 | In progress | NFR files | 2026-10-01-test-project |

## Tổng kết
Done: FR-010 (tìm xe và availability), FR-014 (phân xe/tài xế & đổi xe), FR-018 (vòng đời đơn thuê & quyết toán), FR-022..025 (subscription, gồm cả thanh toán mua gói); In progress: các nhóm còn lại; Not started: các nhóm chưa chạm. `dotnet build -warnaserror` đã xanh 0 warning/0 error; test suite pass 100% (318/318 tests) ngày 2026-10-06.

## Vòng đời đơn thuê, Quá giờ & Quyết toán (FR-018 / BE-17) — 2026-10-06

Đã hoàn thành module BE-17 theo kiến trúc Clean Architecture:
- Migration `0013_rental_lifecycle.sql`: Ràng buộc `ck_rentals_status`, các mốc thời gian vòng đời trên `rentals`, bảng `rental_status_history` kèm trigger bất biến `trg_rsh_immutable`, bảng quyết toán `rental_settlements`, cột `kind` trên bảng `invoices` kèm partial unique index `ux_invoices_rental_kind`.
- Domain: `RentalStatus`, `RentalTrigger`, `RentalStateMachine`, `OvertimeCalculator` (15 phút grace period, làm tròn lên theo giờ), `CancellationPolicy` (trước 24h miễn phí, dưới 24h giữ 20% phí thuê), `SettlementCalculator` (bù trừ cọc và tiền quá giờ), `MoneyRounding` làm tròn VND tại một chỗ duy nhất bằng `MidpointRounding.AwayFromZero`.
- Application: `RentalLifecycleService`, `RentalOptions`, `RentalCancellationOptions`, các ports `IRentalLifecycleRepository`, `IRentalPaymentGate`, `IInvoiceGateway`, `ISubscriptionUsage`, `ILoyaltyAward`, `IRentalEventSink`, `IRentalQueries`.
- Infrastructure: `NpgsqlRentalLifecycleRepository` với `pg_advisory_xact_lock` và `FOR UPDATE` trong transaction chống tranh chấp đồng thời, `RentalPaymentGateAdapter` tạo refund payment có tính lũy nghiệm, `InvoiceGatewayAdapter` tạo hóa đơn quá giờ `kind = 'overtime'`, `NpgsqlRentalQueries` hỗ trợ xem đơn của tôi, đơn vận hành và đơn quá hạn (`overdue`).
- WebApi: `RentalLifecycleController` và `RentalQueriesController` đầy đủ metadata Swagger, cấu hình phân quyền và ánh xạ thông báo qua `ApiSuccessResponseFilter`. Endpoint cũ `PATCH /api/rentals/{id}/status` của `OperationsController` chuyển thành bí danh deprecated với header `Deprecation: true`.
- Gỡ bỏ hoàn cọc tự động tại inspection (`AddInspectionAsync` / O2): Toàn bộ việc quyết toán cọc và hoàn cọc được chuyển tập trung về `complete`.
- Kiểm thử: 100% tests pass (318/318 tests), bao gồm unit test Domain ma trận 6 trạng thái x 5 lệnh, unit test Service, integration test schema/trigger/index, integration test E2E có quá giờ 90 phút, test shortfall, test hủy đơn và test đua tranh `CompleteAsync` 20 lần lặp liên tiếp không flaky. Xem [thay đổi](../changes/2026-10-06-rental-lifecycle.md) và [ADR-004](decisions/ADR-004-rental-lifecycle-and-settlement.md).

## Phân xe, tài xế & Đổi xe (FR-014 / BE-14) — 2026-10-04

Đã hoàn thành module BE-14 theo kiến trúc Clean Architecture:
- Migration `0012_assignment_constraints.sql`: Ràng buộc từ vựng `ck_va_status`, cột sinh `period tsrange` kèm đệm 60 phút, exclusion constraint `ex_va_driver_no_overlap` chống trùng lịch tài xế, các cột lưu vết (`reason`, `note`, `supersedes_id`, `ended_at`, `starts_at`, `ends_at`), và backfill dữ liệu cũ.
- Domain: `AssignmentStatus`, `AssignmentReason`, `DriverEligibility` (kiểm tra hạn bằng GMT+7, active, is_driver, đệm), `RentalAssignmentPolicy`.
- Application: `IAssignmentRepository`, `IAssignmentService`, `IMaintenanceIncidentGateway`, `AssignmentOptions` (cấu hình đệm, cờ kiểm tra hạng bằng), các record/DTO chuẩn.
- Infrastructure: `NpgsqlAssignmentRepository`, `DriverLock` (advisory lock 1002), khóa xe theo thứ tự ID tăng dần chống deadlock, xử lý đổi xe cập nhật `rentals.vehicle_id` dời `vehicle_bookings`, tự động mở phiếu bảo dưỡng sự cố `vehicle_maintenance` khi xe hỏng (`breakdown`/`accident`), ánh xạ lỗi `23P01` -> `DRIVER_BUSY` (409) / `VEHICLE_UNAVAILABLE` (409).
- WebApi: `RentalAssignmentsController` với 5 endpoints chuẩn Swagger metadata, phân quyền `CustomerService`, mapping message trong `ApiSuccessResponseFilter`, endpoint cũ `POST /api/rentals/{id}/assignments` được đánh dấu `[Obsolete]` kèm header `Deprecation: true`.
- Kiểm thử: 100% tests pass bao gồm Domain tests, Schema integration tests, Concurrency 20x loop test không flaky, API contract tests. Xem [thay đổi](../changes/2026-10-04-assignment-vehicle-swap.md) và [ADR-003](decisions/ADR-003-vehicle-and-driver-assignments.md).

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

## O2/O4/O5/BE-24 — 2026-10-04
FR-031: licensePlate, ca trực, report staff/driver, Manager giao Maintenance, inspection độc lập tài chính, đổi xe đúng số chỗ/đổi tài khi renting và giữ giá. Migration 0011; PostgreSQL test độc lập. Customer report và tính phí bồi thường tự động chưa triển khai. Xem docs/operational-recovery.md.

Kiểm chứng 2026-10-04: build -warnaserror thành công, 101/101 tests pass trên PostgreSQL 18 tạm; chưa chạy migration trên Neon, chưa kiểm chứng Docker deployment.

## Department assignment — 2026-10-05
FR-007/FR-008: Admin assigns Manager/Staff to departments; Manager assigns Staff only. Dedicated user-ID endpoint preserves specialties, protects multi-role Manager/Admin targets, validates references, and revokes credentials. Manager can read staff/departments; personnel CRUD remains Admin-only. All 112 tests pass against isolated PostgreSQL 18. No new migration. See [change](../changes/2026-10-05-department-assignment.md).

## Facility assignment — 2026-10-05
FR-007/FR-008: Admin assigns Staff/Manager to facilities; Manager assigns Staff only. Department and other employee fields are preserved. Shared organization-assignment logic validates references and protected roles, then invalidates credentials. Verification: 116/116 tests pass with isolated PostgreSQL 18; no application database changes or new migration. See [change](../changes/2026-10-05-facility-assignment.md).
## Phân công kiểm tra trước thuê và readiness bàn giao — 2026-10-21

FR-015: đã thêm GET/PUT `/api/rentals/{id}/inspection-assignment`, persist Maintenance active theo rental/xe hiện tại và người/thời gian giao; chỉ Admin/Manager phân công trước evidence/chuyến. Maintenance được đọc việc được giao, quyền ghi inspection hiện tại giữ nguyên. Flat GET `/api/rentals/{id}` và nested `/api/rentals/{id}/detail` đều có boolean `hasPassingPreInspection`/`hasPostInspection` theo xe hiện tại để Admin không đề nghị bàn giao khi chưa kiểm tra đạt. Invariant start/complete vẫn do server kiểm tra.

Kiểm chứng: **110/110 tests pass, 0 skipped**, build mọi project với warnings-as-errors theo cấu hình repo; PostgreSQL 18 test database độc lập áp dụng toàn bộ migration, connection timezone UTC. Bao gồm `InspectionAssignmentTests`, `StaffAuthorizationTests`, `RentalLifecycleServiceTests`, `RentalLifecycleIntegrationTests`, `RentalOperationsApiTests`, `ApiResponseTests`. HTTP thật xác minh flat rental readiness/evidence, assignment null/assigned, quyền, validation, envelope 6 trường và Swagger; staff/driver list null-search + pagination chạy thành công tại SQL và HTTP, chưa tái hiện được lỗi 500 production trong ảnh. `git diff --check` sạch. Migration `0025_inspection_assignments.sql` được runner tự phát hiện; chưa deploy/chạy migration production hoặc kiểm chứng Docker. Xem [thay đổi](../changes/2026-10-21-inspection-dispatch-readiness.md).
## Danh sách kiểm tra và người bảo trì xe — 2026-10-21

Đã triển khai GET `/api/inspections` theo inspectionType/rentalId và pagination mặc định 1/20, tối đa 100; gồm toàn bộ evidence xe cũ và rental đã đóng, có email người kiểm tra và thông tin xe. Vehicle list/detail có `maintenanceTechnicianEmails` theo Maintenance active và trạng thái phiếu hiện tại; ẩn email khỏi public/customer/Driver/CustomerService. Thêm test mở khóa user rồi đọc lại từ repository mới để xác nhận trạng thái PostgreSQL bền vững. Không thêm migration ngoài 0025 của tính năng phân công trước đó. Kiểm chứng root: build 0 warnings/errors; **75/75 tests pass, 0 skipped** trên PostgreSQL 18 độc lập (`Timezone=UTC`), gồm OperationalDirectoryTests, InspectionAssignmentTests, UserAdministrationTests, UserAdministrationIntegrationTests, VehicleSearchTests và OperationsCompletionApiTests. Bao phủ HTTP/DB, filter/pagination/auth/envelope/Swagger, current technician email/public redaction và unlock persistence. Root xác nhận read-only live Railway GET phân công trả 404 và Swagger chưa có route; FE dev rewrite đang trỏ origin live này và giữ nguyên theo yêu cầu. Source GET chưa phân công có regression 200/null; cần deploy backend/migration đồng bộ để route tồn tại trên môi trường đó. Chưa deploy/Docker. Xem [thay đổi](../changes/2026-10-21-inspection-directory-maintenance-emails.md).

## GPLX chưa gửi của khách hàng mới — 2026-10-22

FR-006: GET GPLX trả status=none nếu số GPLX null/rỗng/khoảng trắng, không biến giá trị pending mặc định khi đăng ký thành hồ sơ đã gửi. Giữ bản ghi để upload lần đầu; hồ sơ đã gửi giữ trạng thái duyệt và đọc không mutate DB. Kiểm chứng CustomerLicenseUploadTests: **47/47 pass, 0 skipped** với HTTP/envelope/Swagger và upload đầu tiên, build Release thành công. Không migration/deploy. Xem [thay đổi](../changes/2026-10-22-unsubmitted-customer-license.md).
## 2026-10-22 — Phân công/inspect sau trả xe

API assignment hỗ trợ inspectionType=post_rental, Admin checked hoặc Manager phân công riêng theo giai đoạn; migration 0029 giữ hồ sơ trước thuê, post không tự complete/paid hoặc ghi đè evidence. Build Release/54 tests liên quan PostgreSQL đạt, HTTP auth/query/envelope/Swagger đã kiểm tra. Chưa deploy. Xem [thay đổi](../changes/2026-10-22-post-inspection-dispatch.md).

## 2026-10-23 — Nhiều GPLX
- Implemented: migration 0030, independent license ID/save/review, vehicle allowedLicenseTypes, eligibility through rental end across booking/edit/assignment/handover, pending queue/filter across licenses.
- Added: HTTP/validator tests and PostgreSQL regression tests. Deployment requires backend restart to apply migration 0030 with frontend update.
