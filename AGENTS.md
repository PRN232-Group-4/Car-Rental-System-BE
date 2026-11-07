# Hướng dẫn phát triển Car Rental System

## Mục tiêu và phạm vi
Đây là **system** cho một đơn vị vận hành sở hữu đội xe; không phải marketplace đa chủ xe. Không thêm owner/partner/commission nếu chưa có ADR mới. Đọc `docs/`, `specs/`, `changes/` trước khi sửa mã.

## Kiến trúc bắt buộc
Giữ dependency một chiều: `Domain <- Application <- Infrastructure <- WebApi`.

- `Domain`: `Entities/`, `ValueObjects/`, `Services/`, `Interfaces/`; không EF/Npgsql/HTTP/Redis. Domain port chỉ được dùng Domain types/kiểu cơ bản.
- `Application`: `Services/` điều phối use case; `Interfaces/` cho service/repository/provider ports; `DTOs/` cho input/output/options; `Validators/` cho input rules. Không phụ thuộc Infrastructure/WebApi.
- `Infrastructure`: `Persistence/` cho DbContext/row; `HostedServices/` cho migration và job nền; `Repositories/` chứa SQL/mapping/transaction; `Services/` chứa adapter Redis/Brevo/PayOS/Cloudinary.
- `WebApi` (`VehicleRental.WebApi`): `Controllers/`, `Middlewares/` và composition root duy nhất. DTO ở Application; HTTP auth/response/filter hỗ trợ nằm dưới Middlewares.

Repository chỉ đại diện aggregate root. Không generic repository; controller không gọi Npgsql/Redis trực tiếp. Schema cố ý không có FK: Application phải xác minh logical reference và transaction khi đổi nhiều bảng.

Controller gọi Application service interface, không inject Repository hoặc Redis implementation.
Service không kế thừa repository interface; SQL chỉ nằm ở Infrastructure. Khi chuyển invariant đang
đọc dữ liệu dưới lock sang Domain/Application, giữ nguyên transaction boundary và cơ chế chống race.

## Quy tắc nghiệp vụ bắt buộc
- Xác thực: email lower-case; OTP 6 số có TTL, cooldown, giới hạn sai và rate limit email/IP. Không log OTP, API key, mật khẩu, cookie hoặc connection string. Tạo `users`/`customers` chỉ sau verify thành công.
- Mật khẩu: đăng ký/đăng nhập và mật khẩu mới khi tạo nhân viên/đổi/reset phải từ **6 đến 20 ký tự** (bao gồm hai mốc), dùng giới hạn chung trong `PasswordRules`. Không cắt ngắn hoặc trim mật khẩu để vượt qua validation. PBKDF2/Argon2 có salt; đổi/reset mật khẩu thu hồi mọi session.
- Session: cookie `HttpOnly`, `Secure`; `SameSite=Lax` mặc định, `SameSite=None` khi request có header `Origin` (web cross-site); ticket Redis với absolute + sliding expiry; logout xóa ticket.
- Thuê xe: không chồng lịch; long-term khóa xe; pre-inspection đạt trước `renting`; post-inspection trước `completed`; fail khóa xe, staff report riêng, Manager giao maintenance (FR-031).
- Payment: subscription rental không invoice; direct rental phải qua invoice; snapshot giá/policy lúc đặt.

## Contract response API bắt buộc

Mọi response JSON của API, bao gồm thành công và lỗi, phải có đủ **6 trường ở cấp ngoài cùng**: `code`, `status`, `is_success`, `data`, `message`, `error`. Không bỏ trường có giá trị `null`.

| Trường | Quy tắc |
|---|---|
| `code` | Số nguyên, khớp mã HTTP thực tế của response. |
| `status` | Chuỗi `success` khi thành công, `error` khi lỗi. |
| `is_success` | JSON boolean `true`/`false`; tuyệt đối không dùng string hoặc số. |
| `data` | Dữ liệu nghiệp vụ khi thành công; `null` khi lỗi. |
| `message` | Chuỗi thông báo đúng ngữ cảnh thao tác/lỗi. |
| `error` | `null` khi thành công; chi tiết lỗi khi thất bại. Giữ ProblemDetails/ValidationProblemDetails và `errors` bên trong trường này. |

- Dùng contract chung trong [ApiResponse.cs](src/VehicleRental.WebApi/Middlewares/Responses/ApiResponse.cs); không tự tạo format khác hoặc bọc envelope hai lần.
- Controller trả dữ liệu/kết quả bình thường để [response filter](src/VehicleRental.WebApi/Middlewares/Responses/ApiSuccessResponseFilter.cs) chuẩn hóa. Minimal API hoặc kết quả ngoài MVC phải dùng factory `ApiResponse.Create`/`ApiResponse.Failure` với code đúng HTTP.
- Bao phủ lỗi nghiệp vụ, validation tự động, JSON không hợp lệ, authentication/authorization, exception và route/method không hợp lệ. Không để lỗi 401/403/404/405 trả body rỗng; giữ các header giao thức như `WWW-Authenticate`, `Allow` và `Set-Cookie`.
- Giữ hành vi hiện tại: empty success/NoContent trả 200 có envelope; Created có dữ liệu trả 201, Accepted trả 202. Không trả 204 cho thao tác cần body JSON.
- Không đưa stack trace, exception nội bộ hoặc secret vào lỗi 500; dùng thông báo an toàn.
- Redirect OAuth/HTTPS, HTML, Swagger/OpenAPI và các response giao thức không phải JSON API giữ định dạng riêng; không bọc chúng thành envelope.
- Khi thêm/sửa endpoint hoặc contract, cập nhật [Swagger response filter](src/VehicleRental.WebApi/Middlewares/Responses/ApiSuccessResponseOperationFilter.cs), [docs/API.md](docs/API.md) và test liên quan. Swagger phải khai báo đủ 6 trường bắt buộc cho success/error, `code` là integer và `is_success` là boolean.
- Test thay đổi response phải kiểm tra JSON thực tế: đủ 6 trường kể cả null, đúng kiểu boolean, code khớp HTTP và không double wrap. Dùng [ApiResponseTests.cs](tests/VehicleRental.Tests/ApiResponseTests.cs) cho kiểm chứng HTTP/serialization; không chỉ kiểm tra đối tượng C#.

Nguồn quyết định: [NFR-008](specs/NFR-008-openapi.md), [ADR-002](docs/decisions/ADR-002-api-response-envelope.md).

## Pagination bắt buộc cho API danh sách

- Mọi API trả về danh sách phải có pagination. Khi thêm hoặc sửa API list, bắt buộc áp dụng contract này.
- Query mặc định `page=1`, `pageSize=20`; `page >= 1`, `pageSize` từ **1 đến 100**.
- `data` trong envelope chung phải chứa đủ **`items`, `page`, `pageSize`, `totalCount`**; không trả array trực tiếp. `totalCount` là tổng bản ghi sau lọc, trước phân trang.
- Validate pagination tại **HTTP boundary và Application service**. Giá trị sai trả **400** theo contract lỗi chung; không tự clamp giá trị sai.
- Dùng thứ tự sắp xếp ổn định; tính offset tránh tràn số nguyên. Trang vượt phạm vi trả `items=[]`, giữ `page`, `pageSize`, `totalCount` chính xác.
- Swagger và docs/API.md phải mô tả default/range và page schema. Test liên quan phải bao phủ mặc định, giới hạn 1/100, giá trị không hợp lệ, tổng sau lọc và trang rỗng.

Nguồn quyết định: [NFR-008](specs/NFR-008-openapi.md), [ADR-003](docs/decisions/ADR-003-list-pagination.md).

## Quy trình mỗi thay đổi
1. Đọc/cập nhật spec FR/NFR trước.
2. Làm use case nhỏ nhất, thêm test Domain/Application hoặc integration khi dùng DB/Redis.
3. Cập nhật acceptance criteria và `docs/progress.md`.
4. Tạo `changes/YYYY-MM-DD-<slug>.md`, cập nhật `changes/CHANGELOG.md`.
5. Giả định vào `docs/open-questions.md`; quyết định bền vững vào ADR; tối ưu để sau vào backlog.

## Definition of Done
- Validate ở HTTP boundary, invariant ở Application/Domain; success/error JSON tuân thủ contract 6 trường ở trên, lỗi giữ ProblemDetails bên trong `error`.
- API danh sách tuân thủ pagination chung: mặc định page=1/pageSize=20, tối đa 100, đủ items/page/pageSize/totalCount và validation ở HTTP/Application.
- Mỗi API mới phải xuất hiện trong Swagger ở non-production: controller dùng `ApiController`, route rõ ràng, request/response status codes và auth requirement được mô tả. Không ẩn endpoint khỏi Swagger trừ khi có lý do bảo mật được ghi trong spec.
- Mỗi API mới phải trả success message đúng ngữ cảnh nghiệp vụ, không dùng message chung chung. Bổ sung mapping `Controller.Action` tương ứng trong `src/VehicleRental.WebApi/Middlewares/Responses/ApiSuccessResponseFilter.cs` và kiểm tra message phản ánh đúng hành động (ví dụ: tạo, cập nhật, phê duyệt, hủy hoặc truy xuất).
- Không secret trong Git; Options `ValidateOnStart`, `.env.example` đầy đủ.
- Build/test/Docker chạy; container non-root.
- Specs, changes và progress phản ánh đúng đã làm/chưa làm.

## Khi chưa rõ
Không dừng lại: chọn phương án an toàn, đơn giản, dễ đổi và ghi giả định/lý do. Chỉ hỏi khi quyết định đổi phạm vi nghiệp vụ hoặc chi phí/hạ tầng.
