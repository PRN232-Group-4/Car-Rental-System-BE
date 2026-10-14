# NFR-007 — Kiểm thử và coverage >= 90% toàn bộ BE (BE-30)

- **Status:** In progress; **Priority:** P0 (nâng từ P1); **Milestone:** M8; **Assignee:** Huy
- **Phạm vi:** **toàn bộ BE** (kể cả module của Lạc, Phúc, Kiệt, Anh). Chia hai bước: **BE-30a** (hạ tầng test + công cụ đo + cổng CI theo nấc) làm **đầu tiên**, trước BE-10; **BE-30b** (lấp khoảng trống đến 90%) làm **cuối cùng**, sau khi các module khác xong.
- **Phụ thuộc:** BE-30a không phụ thuộc gì; BE-30b phụ thuộc mọi module BE.

## 1. Hiện trạng (khảo sát)

* Một project test `tests/VehicleRental.Tests` (xUnit 2.9.2, **21 test**: 17 integration + 4 unit); không có FluentAssertions/Moq/NSubstitute/Testcontainers; chỉ `Assert.*`.
* `PostgresFixture` (`IAsyncLifetime`): mỗi test class tạo DB tạm `rental_test_{Guid}`, nạp `docs/db/Init.sql`, kết nối qua `TEST_DATABASE_ADMIN` hoặc `localhost:5432`. **Cần xác nhận** có nạp `db/migrations/*.sql` không (khảo sát cho thấy `Init.sql` thiếu cột do migration thêm).
* Test gọi thẳng repository, **bỏ qua** HTTP/cookie/Redis. Chưa có `WebApplicationFactory`, chưa có architecture test, chưa đo coverage. CI chạy `dotnet test` với service container `postgres:16-alpine` và `redis:7-alpine`, không có ngưỡng.
* Môi trường dev của nhóm: Docker CLI có thể không có sẵn (Windows) — fixture phải xử lý.

## 2. Quyết định đã chốt

| # | Quyết định | Nguồn |
|---|---|---|
| D1 | Cài **Testcontainers.PostgreSql** (và Testcontainers.Redis) vào project test | Huy |
| D2 | **Coverage >= 90% toàn bộ BE** | Huy |
| D3 | Dùng `TimeProvider` (có sẵn .NET 8) thay `IClock` tự viết; test dùng `FakeTimeProvider` | Thiết kế |
| D4 | Không dùng FluentAssertions (giấy phép thương mại từ v8): dùng `Assert` của xUnit + helper nhỏ | Thiết kế |
| D5 | Ưu tiên **integration trên PostgreSQL thật** vì bất biến nằm trong SQL (trigger, CHECK, exclusion); mock chỉ cho cổng ngoài (Cloudinary, Brevo, PayOS, Google) | NFR-007 gốc |

## 3. Định nghĩa "đạt 90%"

Đo bằng **coverlet** (Cobertura) cho 4 assembly `VehicleRental.Domain/Application/Infrastructure/WebApi`, gộp bằng ReportGenerator:

| Chỉ số | Cổng cứng (CI fail nếu thấp hơn) |
|---|---|
| Line coverage **toàn bộ** | **>= 90%** |
| Branch coverage toàn bộ | >= 80% |
| Line coverage **từng assembly** | >= 85% (chặn việc "giấu" assembly yếu sau assembly mạnh) |

Loại trừ **chỉ** những mục sau, liệt kê tên trong `docs/testing.md` kèm lý do (mọi loại trừ mới phải qua review): mã sinh tự động (`GeneratedCode`, `CompilerGenerated`), `[ExcludeFromCodeCoverage]` đặt có chủ đích trên DTO thuần dữ liệu không có logic, và tệp composition root `Program.cs` **chỉ** phần dựng host (phần cấu hình logic như error handler, auth, rate limit **phải** tách ra lớp riêng có test). Cấm hạ ngưỡng hay mở rộng loại trừ để "cho qua" — thiếu coverage thì viết test.

**Nấc tăng dần (ratchet):** `COVERAGE_MIN_LINE` trong `ci.yml` đặt bằng mức đo thực tế sau BE-30a (làm tròn xuống), rồi tăng từng nấc 60 → 75 → 85 → 90 khi các module hoàn thiện; PR nào làm tụt coverage dưới nấc hiện tại đều fail. Nấc cuối 90 bắt buộc trước khi đóng BE-30b.

## 4. BE-30a — Hạ tầng test (làm trước)

### 4.1 Gói & cấu trúc

```text
tests/VehicleRental.Tests/
  Infrastructure/   DatabaseFixture, RedisFixture, ApiFactory, TestAuth, Fakes, Builders/Seed
  Domain/           unit thuần (không I/O)
  Application/      service với fake cổng
  Persistence/      integration PostgreSQL (repository, trigger, constraint, migration)
  Api/              WebApplicationFactory (HTTP, auth, error mapping, Swagger)
  Architecture/     NetArchTest
  Contracts/        quét metadata controller
```

Gói thêm: `Testcontainers.PostgreSql`, `Testcontainers.Redis`, `Microsoft.AspNetCore.Mvc.Testing`, `Microsoft.Extensions.TimeProvider.Testing`, `NetArchTest.Rules`, `coverlet.collector`. Công cụ: `dotnet-reportgenerator-globaltool` qua `.config/dotnet-tools.json`. Kiểm tra bản mới nhất tương thích .NET 8 bằng `dotnet package search` / nuget.org trước khi ghim; ghim phiên bản trong `Directory.Packages.props` hoặc csproj.

### 4.2 `DatabaseFixture` (thay `PostgresFixture`)

* **Thứ tự chọn nguồn:** (1) biến `TEST_DATABASE_ADMIN` (kết nối admin tới Postgres có sẵn) nếu đặt; (2) Testcontainers (`postgres:16-alpine`, bật extension `btree_gist`, `pg_trgm`) nếu Docker khả dụng; (3) ngược lại **thất bại nhanh** với thông báo rõ ràng, hướng dẫn cả hai cách. Một container dùng chung cho cả assembly (collection fixture).
* **Template DB:** tạo một lần `rental_template`, nạp `docs/db/Init.sql` rồi áp **toàn bộ `db/migrations/*.sql` theo thứ tự bằng đúng runner/cơ chế checksum của production (DbUp)** — nhờ vậy migration được kiểm thử như thật và lệch giữa Init.sql và migration bị phát hiện. Mỗi test class: `CREATE DATABASE rental_t_{guid} TEMPLATE rental_template` (nhanh), drop khi xong.
* Cung cấp `NpgsqlDataSource` theo class, helper `ExecuteAsync`, `QueryAsync`, và hàm **chụp bất biến** (`AssertInvariantsAsync`: mọi `assigned` khớp xe đơn; `v_loyalty_reconciliation` rỗng; không `vehicle_bookings` chồng lấn…) để các test module gọi lại.
* `RedisFixture`: container `redis:7-alpine` (hoặc `TEST_REDIS`), mỗi class dùng một tiền tố khóa riêng/`FLUSHDB` trên DB index riêng; không dùng chung trạng thái giữa class.

### 4.3 `ApiFactory` (WebApplicationFactory<Program>)

* Thêm `public partial class Program;` ở `Program.cs`.
* `ConfigureTestServices`: thay `NpgsqlDataSource` bằng DB của fixture; Redis bằng container; **fake** `IImageStorage` (Cloudinary), gửi email Brevo (ghi hộp thư trong bộ nhớ để assert), client PayOS (trả kết quả cấu hình được, và test chữ ký webhook thật), Google auth; `TimeProvider` → `FakeTimeProvider`.
* Hai kiểu xác thực: (1) `TestAuthHandler` — `client.AsRole("Admin"|"Manager"|"Staff"|"Maintenance"|"Customer", userId)` cho đa số test; (2) **luồng thật** qua `/api/session` + Redis ticket store cho nhóm test phiên: đăng nhập → cookie `__Host-vehicle-rental` → `GET /api/account/profile` → đăng xuất → 401; đổi mật khẩu thu hồi mọi phiên; sliding/absolute expiry với `FakeTimeProvider`.
* Helper `Seed` dùng builder có seed cố định (không `Random` không seed, không `DateTime.Now`).

### 4.4 Thay thế `DateTime.UtcNow`

Đăng ký `builder.Services.AddSingleton(TimeProvider.System)`; refactor các repository/service **đang gọi `DateTime.UtcNow`** nhận `TimeProvider`. Đây là thay đổi chạm vào mã của người khác: làm thành **PR nhỏ riêng**, không đổi hành vi, báo chủ module; chỉ sửa tối thiểu để test được.

### 4.5 Kiểm thử kiến trúc & hợp đồng (đóng mục "Remaining" của NFR-001/008)

* `Architecture/`: Domain không tham chiếu Application/Infrastructure/WebApi/Npgsql/StackExchange.Redis/AspNetCore; Application không tham chiếu Infrastructure/WebApi/Npgsql/Redis; Infrastructure không tham chiếu WebApi; **controller không dùng `NpgsqlDataSource`/`IConnectionMultiplexer`**; không lớp nào trong Domain/Application dùng `DateTime.UtcNow`/`DateTime.Now` (kiểm bằng quét IL hoặc quy tắc `NetArchTest` + analyzer; nếu quá phức tạp thì dùng test quét mã nguồn).
* `Contracts/`: **mọi action** có ánh xạ trong `ApiSuccessResponseFilter` (agent.md bắt buộc); mọi action khai báo `[ProducesResponseType]` thành công + lỗi; **an toàn mặc định** — không action nào thiếu `[Authorize]` mà cũng không có `[AllowAnonymous]` (danh sách `AllowAnonymous` chụp lại thành snapshot để review thay đổi); Swagger sinh ra không lỗi và chứa mọi route; mọi `Options` có `ValidateOnStart`; ProblemDetails có `code` cho lỗi nghiệp vụ.

### 4.6 Đo coverage và CI

* `coverage.runsettings` (coverlet collector): định dạng Cobertura, `Include=[VehicleRental.*]*`, `ExcludeByAttribute=GeneratedCodeAttribute,CompilerGeneratedAttribute,ExcludeFromCodeCoverageAttribute`, loại trừ theo mục 3.
* Script `scripts/coverage.sh` và `scripts/coverage.ps1`: chạy `dotnet test --settings coverage.runsettings --collect:"XPlat Code Coverage"`, gộp bằng ReportGenerator (`-reporttypes:Html;JsonSummary;MarkdownSummaryGithub`), **đọc JsonSummary và so với ngưỡng** (line toàn bộ, branch toàn bộ, line từng assembly) → mã thoát khác 0 nếu thấp.
* `ci.yml`: giữ service `postgres`/`redis` làm đường dự phòng bằng `TEST_DATABASE_ADMIN`/`TEST_REDIS` **hoặc** bỏ và để Testcontainers (ubuntu-latest có Docker) — chọn một, ghi vào ADR; thêm bước chạy `scripts/coverage.sh` với `COVERAGE_MIN_LINE`, đẩy `MarkdownSummaryGithub` vào `$GITHUB_STEP_SUMMARY`, upload báo cáo HTML/Cobertura làm artifact, cache NuGet. Thời gian toàn bộ suite mục tiêu <= 8 phút trên CI.
* Đặt `[Trait("Category","Unit"|"Integration"|"Api"|"Architecture")]` để chạy lọc nhanh khi phát triển.

## 5. BE-30b — Lấp khoảng trống đến 90%

1. Chạy `scripts/coverage.sh`, xuất **danh sách file theo số dòng chưa phủ giảm dần** vào `docs/testing.md` (bảng "Khoảng trống").
2. Viết test theo khu vực, đo lại sau mỗi khu vực. Tối thiểu phải có:

| Khu vực | Nội dung bắt buộc |
|---|---|
| Registration/OTP (Redis) | TTL, cooldown, giới hạn sai, rate limit email/IP, verify tạo `users`+`customers` mới sau thành công, không log OTP |
| Session/Account | Đăng nhập/đăng xuất, cookie flags, thu hồi phiên, đổi/reset mật khẩu, hồ sơ, GPLX khách |
| RBAC & Organization | Policy theo role, `Manager`, phòng ban/cơ sở/nhân viên, staff & driver, lịch tài xế |
| Vehicles | CRUD loại xe/xe, subtype đúng loại, transition `status`, ảnh Cloudinary (fake) |
| Operations | Tạo rental giờ/dài hạn, policy snapshot, inspection, maintenance |
| FR-010/014/018/029 | Theo test của từng spec (đã viết kèm module) |
| Payments | Invoice, payment, refund + duyệt, subscription plan/mua/dùng/hết hạn, **webhook PayOS** (chữ ký hợp lệ/sai/trùng lặp) |
| Media/Cloudinary, Brevo | Adapter với `HttpMessageHandler` giả: thành công, lỗi mạng, phản hồi sai |
| WebApi | Error handler (404/400/409/500 + `code`), `ApiSuccessResponseFilter`, health (DB + Redis), Swagger/ProblemDetails, rate limit |
| Program/cấu hình | Options `ValidateOnStart` (thiếu cấu hình → khởi động thất bại có thông báo) |

3. **Quy tắc test mã của người khác:** chỉ thêm test; nếu cần chỉnh để test được thì PR nhỏ riêng, không đổi hành vi, gắn tên chủ module để review. Phát hiện bug thật: **không sửa im lặng** — mở issue/ghi `docs/open-questions.md` kèm test tái hiện (`[Fact(Skip="BUG-n: …")]` chỉ khi chủ module xác nhận sẽ sửa; mặc định để test đỏ trên nhánh riêng để báo cáo).
4. Chất lượng test (tránh "phủ giả"): mỗi test có assert về **hành vi/dữ liệu**, không chỉ "không ném lỗi"; kiểm tra cả đường lỗi; không `Thread.Sleep`, không phụ thuộc thứ tự chạy; mọi mốc giờ qua `FakeTimeProvider`; dữ liệu seed xác định; mỗi class DB riêng nên chạy song song được.

## 6. Tài liệu

`docs/testing.md`: cách chạy (Docker Desktop **hoặc** `TEST_DATABASE_ADMIN`/`TEST_REDIS` trỏ Postgres/Redis cục bộ), cấu trúc test, quy ước đặt tên (`Method_Condition_Expected`), danh sách loại trừ coverage + lý do, bảng khoảng trống, cách thêm test cho module mới, Definition of Done liên quan test (đã có trong agent.md, bổ sung: "PR phải giữ coverage >= nấc hiện tại"). ADR mới: "Testcontainers + TimeProvider + cổng coverage". Cập nhật `agent.md` (Definition of Done: test + coverage), `specs/NFR-007`, `docs/progress.md`, `changes/…`, CHANGELOG.

## 7. Acceptance criteria

**BE-30a**
- [ ] Testcontainers cho Postgres + Redis; có đường `TEST_DATABASE_ADMIN`/`TEST_REDIS` và thông báo lỗi rõ khi không có Docker
- [ ] Template DB nạp Init.sql **và toàn bộ migration** bằng runner production; test hiện có chạy xanh trên fixture mới
- [ ] `WebApplicationFactory` + `TestAuthHandler` + luồng phiên thật qua Redis; fake cổng ngoài
- [ ] `TimeProvider` được tiêm; không còn `DateTime.UtcNow` trong Domain/Application (và trong mã mới)
- [ ] Architecture test + Contracts test chạy xanh (hoặc đỏ có chủ đích kèm danh sách vi phạm để sửa)
- [ ] Coverlet + ReportGenerator + script cổng; CI có ngưỡng theo nấc, step summary, artifact
- [ ] `docs/testing.md`, ADR

**BE-30b**
- [ ] **Line >= 90% toàn bộ**, branch >= 80%, mỗi assembly >= 85% — cổng CI xanh
- [ ] Danh sách loại trừ không mở rộng ngoài mục 3
- [ ] Không test phủ giả (review mẫu 20 test ngẫu nhiên)
- [ ] Suite chạy <= 8 phút CI, không flaky (3 lần chạy liên tiếp xanh)
- [ ] Bug phát hiện được ghi lại, không bị che bằng skip không lý do

## 8. Câu hỏi mở / giả định

* **O1.** Giữ service container trong `ci.yml` hay chỉ Testcontainers? Mặc định: Testcontainers, giữ biến môi trường làm đường dự phòng.
* **O2.** `Program.cs`: cần tách cấu hình logic ra lớp riêng để đo coverage; đây là refactor chạm Lạc (BE-01) — làm bằng PR nhỏ.
* **O3.** Chia 2 project (Unit / Integration) nếu thời gian chạy vượt 8 phút; hiện giữ 1 project + trait.

---

## Prompt thực thi

### BE-30a (chạy ĐẦU TIÊN — repo BE)

```text
Bạn là senior .NET test/infra engineer. Hiện thực BE-30a theo ĐÚNG specs/NFR-007-testing.md mục 4 (đã cập nhật). Nhánh feat/be-30a-test-infra từ main mới nhất.

ĐỌC TRƯỚC: agent.md; specs/NFR-001, NFR-007, NFR-008; tests/ (PostgresFixture, Seed, Scenarios, UnitTests, từng *Tests.cs); .github/workflows/ci.yml; Program.cs (toàn bộ); ApiSuccessResponseFilter + ApiSuccessResponseOperationFilter; db/migrations/README.md và cơ chế DbUp chạy migration (tìm nơi gọi); docs/db/Init.sql.

BƯỚC 0 (không sửa gì): chạy các lệnh sau và dán kết quả thật: `docker ps` (nếu Docker có), `dotnet --info`, `dotnet test` (nêu số pass/fail và cách DB test được cấp: Testcontainers hay TEST_DATABASE_ADMIN). Trả lời: PostgresFixture có áp db/migrations không? Program.cs đã có `partial class Program` chưa? Nơi nào dùng DateTime.UtcNow (đếm theo thư mục)? Sau đó tiếp tục, nếu Docker không có thì dùng Postgres/Redis cục bộ qua biến môi trường và NÓI RÕ.

QUY TẮC: không đổi hành vi production; mọi thay đổi vào mã production phải tối thiểu để test được (public partial class Program, tiêm TimeProvider, tách cấu hình khỏi Program.cs) và đi thành commit riêng, mô tả rõ. Không dùng FluentAssertions. Ghim phiên bản gói sau khi kiểm tra tương thích net8.0. File .sql/.sh dùng LF.

THỨ TỰ: (1) gói + cấu trúc thư mục; (2) DatabaseFixture + RedisFixture (template DB áp Init.sql + migrations bằng runner production), chuyển test cũ sang fixture mới — 21 test cũ vẫn xanh; (3) ApiFactory + TestAuthHandler + fakes + test phiên thật qua Redis; (4) TimeProvider; (5) Architecture + Contracts test; (6) coverlet + ReportGenerator + scripts/coverage.sh|ps1 + cổng; (7) ci.yml (ngưỡng theo nấc đặt bằng mức đo thực tế); (8) docs/testing.md + ADR.

KIỂM CHỨNG và dán kết quả thật: dotnet build -warnaserror; dotnet test (toàn bộ xanh); scripts/coverage.sh (số line/branch từng assembly); thử cố ý hạ coverage để chứng minh cổng fail (rồi hoàn tác).

BÁO CÁO: file đổi, kết quả lệnh, mức coverage đo được (đặt nấc đầu), các vi phạm kiến trúc/hợp đồng phát hiện (danh sách), rủi ro. Gặp mâu thuẫn: dừng và hỏi.
```

### BE-30b (chạy CUỐI CÙNG — sau khi mọi module BE đã merge)

```text
Bạn là senior .NET test engineer. Hiện thực BE-30b theo specs/NFR-007-testing.md mục 3 và 5: nâng coverage toàn bộ BE lên >= 90% line (branch >= 80%, mỗi assembly >= 85%). Điều kiện tiên quyết: BE-30a đã merge, các module BE khác đã merge. Nhánh feat/be-30b-coverage từ main mới nhất.

BƯỚC 1: chạy scripts/coverage.sh, xuất bảng "file theo số dòng chưa phủ giảm dần" vào docs/testing.md và đưa tôi top 40 file trước khi viết test.
BƯỚC 2: viết test theo từng khu vực ở mục 5, đo lại sau mỗi khu vực, nâng nấc COVERAGE_MIN_LINE trong ci.yml theo từng nấc đã đạt (60 → 75 → 85 → 90). Mỗi khu vực một commit.

QUY TẮC: chỉ thêm test; chỉnh mã production chỉ khi cần để test được, trong PR/commit riêng, KHÔNG đổi hành vi; bug phát hiện được: KHÔNG sửa im lặng, ghi vào docs/open-questions.md kèm test tái hiện và báo cho tôi. Cấm mở rộng danh sách loại trừ coverage, cấm hạ ngưỡng. Không Thread.Sleep; dùng FakeTimeProvider; test phải assert hành vi/dữ liệu, không chỉ "không ném lỗi".

KIỂM CHỨNG và dán kết quả thật: coverage cuối (line/branch toàn bộ và từng assembly), thời gian chạy suite, 3 lần chạy liên tiếp đều xanh, cổng CI xanh.

BÁO CÁO: bảng coverage trước/sau theo khu vực, danh sách bug phát hiện, các test nghi ngờ phủ giả đã tự rà.
```
