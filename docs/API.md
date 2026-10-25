# Tài liệu API — Car Rental System BE

## Bổ sung 22/10/2026: kiểm tra kèm duyệt yêu cầu

POST `/api/rentals/{id}/inspection-assignment/checked` (Admin) nhận `{technicianStaffId, approveRequest?:boolean=false}`. Explicit true ghi phân công/pass/confirmed_at/history và chuyển pending sang pre_inspection trong cùng transaction; dùng chung writer với confirm, giữ quy tắc order paid/subscription, không tự start/paid. Không ghi đè evidence đã có; pending đã pass dùng `/rentals/{id}/confirm`. Thiếu flag giữ hành vi chỉ kiểm tra. Phân công PUT và driver_default không tự duyệt. Quyền/400/401/403/404/409/envelope giữ nguyên, Swagger có field boolean mới.

## Bổ sung 22/10/2026: quá hạn và thu phụ phí sau trả xe

- GET `/api/rentals?status=overdue&page=1&pageSize=20`: quyền CustomerService (Admin/Manager/CustomerService). Lọc renting chưa có returned_at và hạn trả trước giờ máy chủ, trước count/phân trang; giữ newest ID DESC, page>=1/pageSize 1..100. Item thêm `isOverdue:boolean`, `status` vẫn renting để `/return` hợp lệ. Không tạo thêm rental hay ghi status bằng GET.
- POST `/api/invoices/{invoiceId}/staff-checkout`: cùng quyền, ID dương, không body. Chỉ hóa đơn overtime unpaid/payOS thuộc settlement đã completed. Trả 200 CheckoutResult `{orderCode,paymentLinkId,checkoutUrl}` qua envelope 6 trường; 400 ID sai, 401/403 quyền, 404 không có invoice, 409 sai loại/trạng thái/phương thức/settlement, 503 gateway chưa cấu hình. Không ghi paid; webhook xác minh chữ ký/số tiền mới ghi paid.
- POST `/api/invoices/{invoiceId}/cash-confirmation`: mở rộng cho overtime liên kết settlement completed, chỉ trước checkout_started_at. Số tiền từ DB, không nhận amount/paid từ client. Ghi payment type overtime/method cash, reviewed_by/reviewed_at, invoice paid/method cash nguyên tử và idempotent; không phát hành order mới. Đã reserve online trả 409 PAYMENT_IN_PROGRESS. Quy tắc cash của hóa đơn thuê ban đầu không đổi.
- GET `/api/invoices/{id}` thêm `kind`, `canConfirmCash`, `canCreateStaffCheckout`; cờ là boolean từ server. Overtime chưa quyết toán trả canPay=false/paymentBlockedReason=OVERTIME_INVOICE_REQUIRED. Cash và online serialize cùng lock rental→invoice. Nếu provider lỗi sau reserve, giữ khóa phương thức online để tránh thu trùng.
- Nhận lại xe vẫn `/rentals/{id}/return`, lưu returnedAt; `/complete` sau post-inspection chốt phí/cọc, lưu settlement và shortfallInvoiceId. Đã hoàn tất trả xe không đồng nghĩa đã thanh toán hóa đơn phần thiếu. Xem [FR-037](../specs/FR-037-overdue-collection.md), [ADR-011](decisions/ADR-011-overdue-collection.md).

## Bổ sung 21/10/2026: email thông báo nghiệp vụ

Sau khi lưu thành công, duyệt/từ chối GPLX và hoàn tiền, thay đổi trạng thái đơn thuê, ghi kết quả kiểm tra xe và tạo/xử lý báo cáo sự cố gửi email thông báo cho khách hàng liên quan (báo cáo gửi thêm người báo cáo). Đây là email thông báo theo best effort: lỗi tra cứu người nhận, provider, timeout hoặc hủy gửi được ghi log an toàn và không đổi kết quả thành công của thao tác nghiệp vụ. Không trả lỗi email trên response. Không áp dụng quy tắc bỏ qua lỗi này cho email OTP vì người dùng phải nhận mã để xác thực.

## Bổ sung 21/10/2026: tổng việc chờ xử lý trên menu

`GET /api/admin/navigation-counts` trả envelope chuẩn với `data={rentals,inspections,maintenance,reports,licenses}`; mỗi giá trị là số nguyên không âm, không phải API danh sách nên không có pagination. “Mới” là việc chờ xử lý, không phải chưa đọc. Đếm rentals `pending`; inspections ở `pre_inspection`/`post_inspection` chưa có biên bản tương ứng cho xe hiện tại; maintenance `open`/`scheduled`; reports nhân viên `open`; licenses `pending` có số GPLX khác rỗng và tài khoản tồn tại. Cùng phạm vi toàn hệ thống với danh sách hiện hành.

Admin/Manager nhận đủ tổng; CustomerService nhận rentals/inspections/licenses; Maintenance nhận inspections/maintenance. Riêng reports: Admin/Manager đếm toàn hệ thống; các role nhân viên khác chỉ đếm báo cáo của `staff_id` từ claim, thiếu claim hợp lệ trả 0. Staff chung chỉ nhận reports của mình. Vai trò kết hợp nhận hợp các quyền. Customer/Driver trả 403, chưa đăng nhập trả 401, dùng envelope 6 trường. Không nhận ID người dùng hay quyền từ query.

## Bổ sung 21/10/2026: hồ sơ nhân viên và cơ sở hiện tại

- POST `/api/staff/from-user` dành cho Admin/Manager: `{userId, departmentId?, facilityId?, hiredDate?, licenseNumber?, licenseType?, licenseIssueDate?, licenseExpiryDate?, licenseImageUrl?}`. userId bắt buộc, các ID phải dương. Trả 201 Staff; 400 validation/reference/GPLX, 404 tài khoản thiếu/khóa, 409 đã có hồ sơ, 401/403 quyền. Giữ identity/password/roles, thêm Staff marker, Driver suy ra từ roles lưu thật; non-driver không lưu GPLX. Response dùng envelope 6 trường và message theo ngữ cảnh.
- Staff response thêm `roles: string[]`; `isDriver` phản ánh role Driver thực tế. GET staff/drivers vẫn phân trang theo contract chung.
- GET `/api/account/profile` thêm `facilityName: string|null` từ cơ sở được phân công; mọi role xem cơ sở của chính mình, không mở rộng quyền directory.
- GET/PUT inspection-assignment thêm `technicianEmail`; GET/POST/PUT maintenance thêm `assignedStaffEmail` và `facilityName` nullable để hiển thị trực tiếp.

Cập nhật: **18/10/2026**. Tài liệu được đối chiếu với controller, DTO, cấu hình xác thực, response filter và repository trong mã nguồn hiện tại. Các ví dụ dùng dữ liệu minh họa; thay ID, thời gian, email và URL bằng dữ liệu thật trước khi gọi.

## Mục lục

1. [Quy ước chung](#1-quy-ước-chung)
2. [Xác thực và phân quyền](#2-xác-thực-và-phân-quyền)
3. [Tài khoản và đăng nhập](#3-tài-khoản-và-đăng-nhập)
4. [Upload hình ảnh](#4-upload-hình-ảnh)
5. [Nhân sự và tổ chức](#5-nhân-sự-và-tổ-chức)
6. [Giấy phép lái xe](#6-giấy-phép-lái-xe)
7. [Loại xe và xe](#7-loại-xe-và-xe)
8. [Chính sách và thuê xe](#8-chính-sách-và-thuê-xe)
9. [Gói thuê và gói của khách hàng](#9-gói-thuê-và-gói-của-khách-hàng)
10. [Hóa đơn, thanh toán và hoàn tiền](#10-hóa-đơn-thanh-toán-và-hoàn-tiền)
11. [Ca trực, sự cố và thay xe](#11-ca-trực-sự-cố-và-thay-xe)
12. [Bảo trì, đánh giá và dashboard](#12-bảo-trì-đánh-giá-và-dashboard)
13. [Trạng thái dịch vụ](#13-trạng-thái-dịch-vụ)
14. [Định vị GPS, Bản đồ và Cảnh báo](#14-định-vị-gps-bản-đồ-và-cảnh-báo)
15. [Cấu trúc dữ liệu trả về](#15-cấu-trúc-dữ-liệu-trả-về)
16. [Ví dụ tích hợp frontend](#16-ví-dụ-tích-hợp-frontend)
17. [Phạm vi hiện tại và nguồn đối chiếu](#17-phạm-vi-hiện-tại-và-nguồn-đối-chiếu)

## 1. Quy ước chung

### Pagination bắt buộc cho API danh sách

Contract chung: `page=1`, `pageSize=20` khi bỏ query; `page >= 1`, `1 <= pageSize <= 100`. Validate ở HTTP và Application; dữ liệu sai trả 400, không tự clamp. Response `data` gồm đủ `items`, `page`, `pageSize`, `totalCount` (tổng sau lọc, trước phân trang). Trang ngoài phạm vi trả `items=[]` và metadata chính xác; kết quả được sắp xếp ổn định.

Rule áp dụng cho mọi API list và bắt buộc khi thêm/sửa endpoint. Một số API cũ trong các bảng dưới đây vẫn trả array; chuyển đổi chúng sang pagination còn pending. Xem [ADR-003](decisions/ADR-003-list-pagination.md).

### Địa chỉ và định dạng

- Địa chỉ local trong `launchSettings.json`: `http://localhost:3000`. Đây là địa chỉ cấu hình, không xác nhận server đang chạy.
- Khi triển khai, dùng `https://<backend-domain>` hoặc đường dẫn `/api` qua proxy cùng origin của frontend.
- Phần lớn endpoint bắt đầu bằng `/api`; `/`, `/health`, `/Home` và `/Home/status` là ngoại lệ.
- Swagger chỉ bật ở môi trường **không phải Production**: `/swagger`; OpenAPI JSON mặc định: `/swagger/v1/swagger.json`.
- Body thông thường: `Content-Type: application/json`. Upload dùng `multipart/form-data`.
- JSON dùng **camelCase**, ngoại trừ trường envelope `is_success`.
- ID là số nguyên; ID tham chiếu phải tồn tại. `{id}` trong tài liệu tương ứng route constraint `{id:int}`; `orderCode` tương ứng `{orderCode:long}`.
- Ngày: `YYYY-MM-DD`. Thời gian: ISO 8601; nên gửi UTC, ví dụ `2026-10-10T02:00:00Z` (09:00 giờ Việt Nam).
- `decimal` được biểu diễn bằng JSON number. Ví dụ giá bên dưới dùng VND; response không có trường currency.
- Các bảng endpoint ghi **mã HTTP thành công thực tế** sau response filter. Cột `data` mô tả dữ liệu bên trong envelope, không phải toàn bộ response.
- Ký hiệu `?` trong phần schema nghĩa là giá trị có thể `null`. Hãy gửi đủ các trường chính khi dùng PUT; PUT không phải PATCH.

### Response thành công

Mọi response JSON của API có đủ **6 trường**, kể cả các trường có giá trị null:

| Trường | Kiểu / ý nghĩa |
| --- | --- |
| `code` | number: mã HTTP thực tế, ví dụ 200, 201, 400 |
| `status` | string: `success` hoặc `error` |
| `is_success` | boolean: `true`/`false`, không dùng string hoặc số |
| `data` | Dữ liệu thành công hoặc null |
| `message` | string: thông báo cho client |
| `error` | Chi tiết lỗi dạng object hoặc null khi thành công |

```json
{
  "code": 200,
  "status": "success",
  "message": "Vehicle retrieved successfully.",
  "is_success": true,
  "data": {
    "id": 1,
    "vehicleTypeId": 2,
    "status": "available"
  },
  "error": null
}
```

`message` phụ thuộc thao tác. Mọi object/array thành công từ controller được bọc trong envelope trên. Với thao tác không có dữ liệu, `data` là `null`.

**Lưu ý mã HTTP:** `NoContent()` và `StatusCodeResult` thành công được filter đổi thành **200** có envelope. Vì vậy login/logout, verify OTP, đổi mật khẩu, một số DELETE và thao tác resolve thực tế trả 200. `Accepted()` không có body vẫn trả **202**; `Created(...)` có dữ liệu trả **201**. `code` luôn phản ánh mã HTTP thực tế. Redirect **302** không có envelope JSON; OAuth/HTTPS redirect và nội dung Swagger giữ giao thức riêng.

### Response lỗi

Lỗi dùng cùng envelope, `is_success=false`, `status="error"`, `data=null`. `ProblemDetails`/`ValidationProblemDetails` nằm trong `error`:

```json
{
  "code": 400,
  "status": "error",
  "is_success": false,
  "data": null,
  "message": "End time must be after start time.",
  "error": {
    "title": "Bad Request",
    "status": 400,
    "detail": "End time must be after start time."
  }
}
```

Giá trị `error.type`, `error.title`, `error.traceId` và các trường bổ sung có thể khác theo framework. Lỗi validation giữ chi tiết trong `error.errors`:

```json
{
  "code": 400,
  "status": "error",
  "is_success": false,
  "data": null,
  "message": "One or more validation errors occurred.",
  "error": {
    "title": "One or more validation errors occurred.",
    "status": 400,
    "errors": {
      "Email": ["The Email field is not a valid e-mail address."]
    }
  }
}
```

| HTTP | Ý nghĩa trong BE |
| --- | --- |
| 400 | Validation, trạng thái nghiệp vụ không hợp lệ; `ArgumentException`/`InvalidOperationException` |
| 401 | Chưa đăng nhập, token hết hạn/đã bị thu hồi, login sai; có envelope lỗi |
| 403 | Sai quyền hoặc sai danh tính nhân viên; có envelope lỗi |
| 404 | Không tồn tại hoặc không thuộc phạm vi được phép xem |
| 405 | HTTP method không được endpoint hỗ trợ; giữ header Allow |
| 409 | Email nhân viên/đăng ký trùng, invoice đã paid, checkout chưa thể hủy |
| 429 | Cooldown OTP hoặc vượt số lần kiểm tra OTP |
| 500 | Lỗi không được xử lý; message/error.detail chung, không trả exception nội bộ |
| 503 | PayOS/Google chưa cấu hình ở endpoint có kiểm tra; DB lỗi ở `/health` |

Không mặc định mọi xung đột nghiệp vụ đều là 409: xung đột lịch xe/tài xế thường đi qua `InvalidOperationException` và trả **400**. Lỗi ràng buộc DB không được xử lý riêng có thể trả **500**.

## 2. Xác thực và phân quyền

### Cookie và token

BE dùng JWT access token và refresh token lưu phiên trong Redis. Login thành công đặt cookie; **không trả token trong `data`**.

| Cookie | Thuộc tính | Path |
| --- | --- | --- |
| `access_token` | `HttpOnly`, `Secure`, `SameSite=Lax` | `/` |
| `refresh_token` | `HttpOnly`, `Secure`, `SameSite=Lax` | `/api/auth` |

- Trình duyệt gửi cookie với request; frontend không đọc cookie HttpOnly.
- JWT cũng được chấp nhận qua `Authorization: Bearer <access-token>` nếu client đã có token hợp lệ. Refresh endpoint chỉ đọc cookie `refresh_token`.
- Thời hạn mặc định từ `TokenOptions`: access token 15 phút; refresh token 7 ngày, hoặc 30 ngày khi `rememberMe=true`. Có thể thay đổi bằng cấu hình triển khai.
- Refresh luân chuyển token và đặt lại cookie. Không gửi nhiều request refresh đồng thời với cùng token.
- Logout thu hồi phiên hiện tại; logout-all, đổi/reset mật khẩu thu hồi mọi phiên. Thay quyền hoặc cập nhật/phân công nhân sự cũng có thể làm token cũ mất hiệu lực.
- JWT được kiểm tra với trạng thái phiên Redis và `authorization_version` của account.
- Cookie `Secure` cần môi trường HTTPS phù hợp. Profile local đang dùng HTTP, nên cần cấu hình HTTPS/proxy phù hợp để kiểm thử đầy đủ đăng nhập bằng cookie.
- `Program.cs` hiện chưa cấu hình CORS. Frontend khác origin cần proxy/cấu hình triển khai phù hợp; chỉ đặt `credentials: "include"` không giải quyết toàn bộ CORS và SameSite.

### Ký hiệu quyền dùng trong các bảng

| Ký hiệu | Role/policy thực tế |
| --- | --- |
| Public | Không yêu cầu đăng nhập |
| Login | Bất kỳ account đã xác thực |
| Admin | `Admin` |
| Customer | `Customer` |
| Management | `Admin`, `Manager` |
| CustomerService | `Admin`, `Manager`, `CustomerService` |
| Inspection | `Admin` hoặc `Maintenance`, đồng thời phải có claim `staff_id` |
| Maintenance | `Admin`, `Manager`, `Maintenance` |
| DriverSchedule | `Admin`, `Manager`, `Driver` |
| RentalRead | `Admin`, `Manager`, `CustomerService`, `Maintenance`, `Customer`, `Driver` |
| FinancialRead | `Admin`, `Manager`, `CustomerService`, `Customer` |
| ReportStaff | `Admin`, `Manager`, `Staff`, `Maintenance`, `CustomerService`, `Driver` |

Role `Staff` chỉ đánh dấu nhân viên, không tự cấp toàn bộ quyền vận hành. Department/facility không tự chuyển thành role. Các account nhiều role có quyền cộng dồn.

**Phạm vi dữ liệu:** Customer xem rental/invoice/payment của mình; Driver xem rental có assignment và lịch của mình. Admin/Manager/CustomerService/Maintenance đọc toàn bộ rental theo policy; chỉ Admin/Manager/CustomerService đọc toàn bộ tài chính. Manager có quyền quản lý toàn hệ thống, không bị giới hạn theo department.

## 3. Tài khoản và đăng nhập

| Method | Endpoint | Quyền | Body | HTTP / data |
| --- | --- | --- | --- | --- |
| POST | `/api/account/registrations` | Public | `StartRegistrationRequest` | 202 / null |
| POST | `/api/account/registrations/verify` | Public | `VerifyRegistrationRequest` | 200 / null |
| POST | `/api/account/registrations/resend` | Public | `ResendRegistrationRequest` | 202 / null |
| POST | `/api/account/password-resets` | Public | `PasswordResetStartRequest` | 202 / null |
| POST | `/api/account/password-resets/verify` | Public | `PasswordResetCompleteRequest` | 200 / null |
| GET | `/api/account/profile` | Login | Không | 200 / `AccountResponse` |
| PUT | `/api/account/profile` | Login | `UpdateProfileRequest` | 200 / `AccountResponse` |
| POST | `/api/account/password/otp` | Login | `PasswordChangeStartRequest` | 202 / null |
| POST | `/api/account/password` | Login | `ChangePasswordRequest` | 200 / null |
| POST | `/api/auth/login` | Public | `LoginRequest` | 200 / null + Set-Cookie |
| POST | `/api/auth/refresh` | Cookie refresh | Không | 200 / null + Set-Cookie |
| POST | `/api/auth/logout` | Public, đọc cookie nếu có | Không | 200 / null |
| POST | `/api/auth/logout-all` | Login | Không | 200 / null |
| GET | `/api/auth/google` | Public | Không | 302 sang Google; 503 nếu tắt |
| GET | `/api/auth/google/complete` | Cookie scheme `External` | Không | 302 về frontend + Set-Cookie |

Callback `GET /api/auth/google/callback` do middleware OAuth xử lý trong `Program.cs`, không phải controller để frontend gọi trực tiếp. Redirect URI đăng ký với Google phải dùng đường dẫn callback này tại origin phục vụ luồng đăng nhập. Sau hoàn tất, BE redirect tới `<GoogleAuthentication:SuccessRedirectUrl>/login?google=ok` hoặc `google=error`.

### Body tài khoản

| Schema | Trường JSON và ràng buộc |
| --- | --- |
| `StartRegistrationRequest` | `email: string` hợp lệ; `fullName: string` 2–120 ký tự; `password: string` 6–20 ký tự |
| `VerifyRegistrationRequest` | `email: string`; `otp: string` đúng 6 chữ số |
| `ResendRegistrationRequest` | `email: string` |
| `PasswordResetStartRequest` | `email: string` |
| `PasswordResetCompleteRequest` | `email: string`; `otp: string` 6 chữ số; `newPassword: string` 6–20 ký tự |
| `UpdateProfileRequest` | `fullName: string` 2–120; `gender: string?` tối đa 50; `phone: string?` tối đa 30 |
| `PasswordChangeStartRequest` | `currentPassword: string` bắt buộc |
| `ChangePasswordRequest` | `currentPassword: string` bắt buộc; `otp: string` bắt buộc, đúng 6 chữ số; `newPassword: string` 6–20 |
| `LoginRequest` | `email: string`; `password: string` 6–20; `rememberMe: bool = false` |

Giới hạn mật khẩu bao gồm cả 6 và 20 ký tự; ngoài khoảng trả 400, chi tiết trong `error.errors`, với thông báo `Mật khẩu phải có từ 6 đến 20 ký tự.` Không trim hoặc cắt ngắn mật khẩu. Tài khoản cũ có mật khẩu dài hơn 20 ký tự cần reset về giới hạn mới để đăng nhập.

OTP mặc định hết hạn sau 10 phút, cooldown gửi lại 60 giây và giới hạn kiểm tra sai theo `MaxVerificationAttempts=5`. Resend đăng ký giữ TTL còn lại, không tự kéo dài TTL. User/customer chỉ được tạo sau verify thành công. Email được chuẩn hóa lowercase. Password reset start trả cùng 202 kể cả khi email không tồn tại hoặc còn cooldown.

**Đổi mật khẩu khi đã đăng nhập:** gọi `POST /api/account/password/otp` với `{ "currentPassword": "..." }`. Backend xác minh mật khẩu cũ rồi gửi OTP tới email từ claim; client không chọn email nhận. Sau đó gọi `POST /api/account/password` với `{ "currentPassword": "...", "otp": "012345", "newPassword": "..." }`. Thiếu/sai/hết hạn OTP trả 400, không cập nhật mật khẩu. Lỗi trường nằm trong `error.errors.currentPassword`, `otp`, `newPassword` (validation MVC có thể dùng PascalCase). OTP riêng cho đổi mật khẩu, không dùng mã đăng ký/reset. Gửi lại cùng endpoint OTP sau cooldown thay mã cũ. Redis compare-and-exchange giữ nguyên tử số lần sai và việc tiêu thụ mã một lần. Lỗi gửi email trả 503 và cho phép thử gửi lại. Cooldown/khóa mã trả 429; cả hai endpoint giới hạn tổng 5 request/phút/IP trên mỗi instance. Thành công trả 200, thu hồi tất cả phiên và xóa cookie; frontend chuyển về đăng nhập.

**Giới hạn xác thực:** đăng ký (gửi/xác nhận/gửi lại), đăng nhập và reset mật khẩu (gửi/xác nhận) có giới hạn riêng từng endpoint 5 request/60 giây/IP/instance, kể cả request validation sai. Hai endpoint đổi mật khẩu dùng chung ngân sách 5 request/60 giây/IP/instance. Request thứ 6 trong cửa sổ trả 429 cùng envelope 6 trường và `Retry-After` (giây); hết cửa sổ được gửi tiếp. Ngoài giới hạn IP, đăng nhập và kiểm tra mật khẩu hiện tại khi đổi mật khẩu có counter Redis riêng theo email chuẩn hóa, dùng chung giữa các instance/IP: tối đa 5 lần trong cửa sổ 10 phút từ lần thử đầu. Bốn lần sai đầu trả 401 (login) hoặc 400 (currentPassword); lần sai thứ 5 trả 429, mật khẩu đúng cũng bị từ chối khi counter đang khóa. Mật khẩu đúng trước ngưỡng xóa counter; hết 10 phút tự thử lại được, không có khóa tài khoản vô hạn. Hai counter login/change độc lập. Request đồng thời phải đặt chỗ nguyên tử trước khi kiểm tra account/hash nên không vượt quá 5 lần kiểm tra.

OTP đăng ký/reset/đổi mật khẩu cũng khóa ngay ở lần sai thứ 5 (429), giữ khóa đến hết TTL (mặc định 10 phút) hoặc phát OTP mới sau cooldown (mặc định 60 giây); phát mã mới reset số lần OTP sai. Registration resend giữ TTL còn lại; start đăng ký/reset/đổi mật khẩu tạo TTL mới. Tăng số lần sai và tiêu thụ OTP dùng CAS Redis chống race. Giới hạn IP request hiện lưu trên từng instance; rate limit request phân tán toàn hệ thống theo email/IP vẫn ở backlog.

Các giới hạn cấu hình qua section `Registration` hoặc biến môi trường tương ứng: `Registration__MaxRequests=5` (1–5), `Registration__RequestWindowSeconds=60` (1–3600), `Registration__MaxPasswordAttempts=5` (1–5), `Registration__PasswordAttemptWindowMinutes=10` (1–60), `Registration__MaxVerificationAttempts=5` (1–5). Options được validate khi khởi động; không cấu hình ngưỡng lớn hơn 5. Counter hết hạn theo thời gian cố định từ lần thử đầu; các lần thử khi khóa không kéo dài khóa.

```json
{
  "email": "customer@example.test",
  "fullName": "Nguyễn Văn An",
  "password": "ExamplePass123!"
}
```

Verify OTP:

```json
{ "email": "customer@example.test", "otp": "123456" }
```

Login:

```json
{ "email": "customer@example.test", "password": "ExamplePass123!", "rememberMe": true }
```

## 4. Upload hình ảnh

| Method | Endpoint | Quyền | Multipart field | HTTP / data |
| --- | --- | --- | --- | --- |
| POST | `/api/media/avatar` | Login | `file` | 200 / `StoredImage` |
| POST | `/api/media/vehicle` | Management | `file` | 200 / `StoredImage` |
| POST | `/api/media/staff-license` | Management (Admin/Manager) | `file` | 200 / `StoredImage` |
| POST | `/api/media/inspection` | Inspection | `file` | 200 / `StoredImage` |
| POST | `/api/media/report` | Nhân viên có quyền report + `staff_id` | `file` | 200 / `StoredImage` |

Avatar: file khác rỗng, tối đa **5 MiB**, MIME bắt đầu bằng `image/`. Ảnh xe: tối đa **10 MiB**, cùng điều kiện MIME. Avatar được lưu vào account hiện tại; upload ảnh xe chỉ trả URL/publicId, sau đó dùng URL trong body tạo/sửa xe. Dịch vụ lưu trữ là Cloudinary.

Ảnh bằng lái nhân viên tối đa **5 MiB**; ảnh inspection/report tối đa **10 MiB**. Các endpoint này trả `secureUrl` và `publicId`; dùng `secureUrl` trong `licenseImageUrl` khi tạo/sửa nhân viên hoặc `imageUrls` khi ghi inspection/report. Kiểm tra nội dung ảnh JPEG/PNG/WebP/GIF trước upload. Upload ảnh customer license trực tiếp qua PUT ở mục 6.

```json
{
  "code": 200,
  "status": "success",
  "message": "Avatar uploaded successfully.",
  "is_success": true,
  "data": { "secureUrl": "https://example.com/avatar.jpg", "publicId": "avatars/example" },
  "error": null
}
```

## 5. Nhân sự và tổ chức

| Method | Endpoint | Quyền | Body / query | HTTP / data |
| --- | --- | --- | --- | --- |
| GET | `/api/staff` | Management | `search?`, `page=1`, `pageSize=20` | 200 / `StaffPage` |
| GET | `/api/staff/drivers` | Management | `search?`, `page=1`, `pageSize=20` | 200 / `StaffPage` |
| GET | `/api/staff/{id}` | Management | Không | 200 / `Staff` |
| POST | `/api/staff` | Management (Admin/Manager) | `CreateStaffRequest` | 201 / `Staff` |
| PUT | `/api/staff/{id}` | Management (Admin/Manager) | `UpdateStaffRequest` | 200 / `Staff` |
| DELETE | `/api/staff/{id}` | Management (Admin/Manager) | Không | 200 / null; deactivate |
| GET | `/api/departments` | Management | Không | 200 / `OrganizationUnit[]` |
| POST | `/api/departments` | Admin | `NameRequest` | 201 / `OrganizationUnit` |
| PUT | `/api/departments/{id}` | Admin | `NameRequest` | 200 / `OrganizationUnit` |
| DELETE | `/api/departments/{id}` | Admin | Không | 200 / null |
| PUT | `/api/departments/{departmentId}/members/{userId}` | Management | Không | 200 / `Staff` |
| GET | `/api/facilities` | Public | Không | 200 / `OrganizationUnit[]` |
| POST | `/api/facilities` | Admin | `FacilityRequest` | 201 / `OrganizationUnit` |
| PUT | `/api/facilities/{id}` | Admin | `FacilityRequest` | 200 / `OrganizationUnit` |
| DELETE | `/api/facilities/{id}` | Admin | Không | 200 / null |
| PUT | `/api/facilities/{facilityId}/members/{userId}` | Management | Không | 200 / `Staff` |

`page >= 1`, `pageSize` từ 1 đến 100. `{id}` ở staff là **staff ID**; endpoint members dùng **user ID**. Response `Staff` có cả `id` và `userId`.

### Body nhân sự

`CreateStaffRequest`:

```json
{
  "email": "driver@example.test",
  "fullName": "Trần Văn Bình",
  "password": "ExamplePass123!",
  "phone": "0901234567",
  "departmentId": 1,
  "facilityId": 1,
  "hiredDate": "2026-10-01",
  "isDriver": true,
  "licenseNumber": "GPLX-001",
  "licenseType": "B2",
  "licenseIssueDate": "2024-01-01",
  "licenseExpiryDate": "2030-01-01",
  "licenseImageUrl": "https://example.com/license.jpg",
  "staffRole": "Driver"
}
```

`UpdateStaffRequest` dùng cùng các trường, **bỏ `email` và `password`**. Tên 2–120 ký tự, password 6–20, phone tối đa 30, số/loại/URL giấy phép tối đa 255; URL phải hợp lệ. Department/facility nullable, nếu có phải là ID dương và tồn tại.

`staffRole`: `CustomerService`, `Driver`, `Maintenance` hoặc null. Driver phải có `isDriver=true`, số giấy phép và ngày hết hạn; ngày cấp không sau ngày hết hạn. `isDriver=true` và role null suy ra Driver. Các specialty còn lại cần `isDriver=false`. PUT role null ở non-driver bỏ specialty, giữ marker Staff; gửi role hiện tại nếu muốn giữ.

`NameRequest`: `{ "name": "Điều phối" }`, name 2–255; có `fuelType: string?` tối đa 255 nhưng department không sử dụng. `FacilityRequest`: `{ "name": "Cơ sở Quận 1", "address": "TP.HCM" }`, name 2–255, address nullable tối đa 255.

Phân công members chỉ thay department/facility, giữ specialty và các trường khác. Admin phân công được Staff/Manager; Manager không được phân công đối tượng có role Manager/Admin (403). User thiếu/inactive: 404; đơn vị không tồn tại hoặc đối tượng không phải nhân viên hợp lệ: 400 theo kiểm tra của repository. Sau phân công, tài khoản đích cần đăng nhập lại.

## 6. Giấy phép lái xe

### Danh sách và chi tiết customer

| Method | Endpoint | Quyền | Query | HTTP / data |
| --- | --- | --- | --- | --- |
| GET | `/api/customers` | CustomerService | `search?`, `isActive?`, `licenseStatus?`, `page=1`, `pageSize=20` | 200 / `CustomerPage` |
| GET | `/api/customers/{customerId}` | CustomerService | Không | 200 / `CustomerDetail` |

Policy CustomerService gồm **Admin, Manager, CustomerService**. Danh sách tìm chuỗi theo tên/email/điện thoại, không phân biệt hoa thường; `isActive` bỏ qua thì lấy cả tài khoản active và inactive. `page >= 1`, `pageSize` từ 1 đến 100. Dữ liệu sắp xếp theo customer ID giảm dần (mới nhất trước); trang vượt phạm vi trả `items=[]` và giữ `totalCount`.

`licenseStatus` tùy chọn nhận `pending`, `approved`, `rejected`, `none` (chữ thường); giá trị khác trả 400. Không truyền thì trả mọi trạng thái. Khách chưa có số GPLX (null/rỗng/chỉ khoảng trắng) có `licenseStatus=none` trong danh sách, dù DB mặc định `pending`; bộ lọc `pending` chỉ lấy hồ sơ đã gửi đang chờ duyệt. Kết hợp tất cả bộ lọc trước phân trang; `totalCount` là tổng sau lọc. Ví dụ `/api/customers?licenseStatus=pending&page=1&pageSize=20`.

`customerId` là **ID bảng customers**, khác `userId`. Chi tiết gồm liên hệ, avatar, trạng thái tài khoản, `license` và `loyalty.points`; không chứa mật khẩu/token. ID không dương trả 400, không tồn tại trả 404. Customer thông thường không được truy cập danh bạ này.

### Giấy phép của customer

| Method | Endpoint | Quyền | Body | HTTP / data |
| --- | --- | --- | --- | --- |
| GET | `/api/customer/license` | Customer | Không | 200 / `CustomerLicense` hoặc null |
| PUT | `/api/customer/license` | Customer | Multipart: `number`, `type`, `issueDate`, `expiryDate`, `file` (đều bắt buộc) | 200 / `CustomerLicense` |
| PUT | `/api/customer/license/url` | Customer | JSON `LicenseRequest` | 200 / `CustomerLicense` |
| PUT | `/api/customers/{customerId}/license-status` | CustomerService | `LicenseReviewRequest` | 200 / `CustomerLicense` |

`GET /api/customer/license` trả `status=none` khi customer chưa có số GPLX (null/rỗng/chỉ khoảng trắng), kể cả bản ghi DB cũ mặc định `pending`. Giữ bản ghi và các trường hiện có để customer gửi lần đầu; không có customer thì `data=null`. Hồ sơ có số GPLX giữ nguyên trạng thái xét duyệt. Thao tác đọc không cập nhật dữ liệu.

`PUT /api/customer/license` nhận **multipart/form-data** để Swagger hiện ô chọn file. Ảnh tối đa **5 MiB**, định dạng JPEG/PNG/WebP/GIF. Server upload Cloudinary rồi lưu URL HTTPS vào giấy phép của customer đang đăng nhập, đưa trạng thái về `pending`. Dữ liệu/ảnh không hợp lệ trả 400 trước khi gọi Cloudinary.

```javascript
const licenseForm = new FormData();
licenseForm.append("number", "GPLX-CUSTOMER-001");
licenseForm.append("type", "B2");
licenseForm.append("issueDate", "2024-01-01");
licenseForm.append("expiryDate", "2030-01-01");
licenseForm.append("file", licenseFile);
await callApi("/api/customer/license", { method: "PUT", body: licenseForm });
```

Để dùng URL ảnh đã có, gửi JSON `LicenseRequest` tới **`/api/customer/license/url`**. Client trước đây gửi JSON tới `/api/customer/license` cần chuyển sang route `/url` hoặc FormData; không tự đặt header Content-Type khi gửi FormData.

```json
{
  "number": "GPLX-CUSTOMER-001",
  "type": "B2",
  "issueDate": "2024-01-01",
  "expiryDate": "2030-01-01",
  "imageUrl": "https://example.com/customer-license.jpg"
}
```

Tất cả trường đều bắt buộc: `number`, `type` (tối đa 255), `issueDate`, `expiryDate` và ảnh (`file` cho multipart, `imageUrl` hợp lệ cho JSON). Không chấp nhận null/rỗng hoặc thiếu ngày cấp/ảnh. `issueDate` không sau `expiryDate`; `expiryDate` không trước ngày hiện tại UTC. Thiếu dữ liệu trả 400 trước khi upload hoặc lưu. Multipart yêu cầu chọn file mới mỗi lần gửi. Lưu giấy phép đưa status về `pending`. Duyệt bằng `{ "status": "approved" }` hoặc `{ "status": "rejected" }`. `customerId` là ID hồ sơ customer.

## 7. Loại xe và xe

| Method | Endpoint | Quyền | Body / query | HTTP / data |
| --- | --- | --- | --- | --- |
| GET | `/api/vehicle-types` | Public | Không | 200 / `VehicleType[]` |
| POST | `/api/vehicle-types` | Management | `VehicleTypeRequest` | 201 / `VehicleType` |
| PUT | `/api/vehicle-types/{id}` | Management | `VehicleTypeRequest` | 200 / `VehicleType` |
| DELETE | `/api/vehicle-types/{id}` | Admin | Không | 200 / null |
| GET | `/api/vehicles` | Public | Query bên dưới | 200 / `VehiclePage` |
| GET | `/api/vehicles/{id}` | Public | Không | 200 / `Vehicle` |
| POST | `/api/vehicles` | Management | `VehicleRequest` | 201 / `Vehicle` |
| PUT | `/api/vehicles/{id}` | Management | `VehicleRequest` | 200 / `Vehicle` |
| DELETE | `/api/vehicles/{id}` | Management | Không | 200 / null |

### Tìm kiếm xe

| Query | Kiểu | Mặc định / ý nghĩa |
| --- | --- | --- |
| `vehicleTypeId` | int? | Lọc loại xe |
| `facilityId` | int? | Lọc cơ sở |
| `status` | string? | Lọc trạng thái xe |
| `from` | datetime? | Đầu khoảng kiểm tra xe trống |
| `to` | datetime? | Cuối khoảng kiểm tra xe trống |
| `page` | int | 1; phải >= 1 |
| `pageSize` | int | 20; từ 1 đến 100 |

Khi kiểm tra xe trống, gửi **cả `from` và `to`**, `to > from`. Repository kích hoạt bộ lọc lịch khi `from` có giá trị, loại xe không available, lịch thuê đang hoạt động trùng khoảng và xe có bảo trì không an toàn/chưa kết thúc. Action tìm kiếm hiện chưa kiểm tra riêng cặp thời gian thiếu hoặc đảo ngược, nên client cần gửi cặp hợp lệ.

Ví dụ: `GET /api/vehicles?vehicleTypeId=1&from=2026-10-10T02:00:00Z&to=2026-10-10T05:00:00Z&page=1&pageSize=20`.

### Body xe

`VehicleTypeRequest`:

```json
{ "name": "Ô tô điện", "fuelType": "electric", "type": "car" }
```

Ba trường bắt buộc; name tối đa 255. `type`: `car`/`motorbike`; `fuelType`: `gasoline`/`electric`. Không đổi category của loại đang được xe sử dụng; tạo loại mới rồi sửa từng xe.

`VehicleRequest` (POST/PUT):

```json
{
  "vehicleTypeId": 1,
  "facilityId": 1,
  "status": "available",
  "brand": "Toyota",
  "model": "Vios",
  "color": "Trắng",
  "yearOfManufacture": 2024,
  "hourlyPrice": 150000,
  "longtermPrice": 12000000,
  "description": "Xe 5 chỗ",
  "mainImageUrl": "https://example.com/car.jpg",
  "location": "TP.HCM",
  "licensePlate": "51A-12345",
  "detail": {
    "seatCount": 5,
    "transmission": "automatic",
    "trunkCapacity": 500,
    "galleryImages": ["https://example.com/car-1.jpg"]
  }
}
```

`vehicleTypeId` dương và `status` bắt buộc. Các trường còn lại nullable; giá không âm, `mainImageUrl` là URL, biển số tối đa 255. Category được xác định từ bản ghi vehicle type, không từ tên hiển thị.

| Nhóm `detail` | Trường và kiểu |
| --- | --- |
| Ô tô | `seatCount: int?`, `transmission: string?`, `trunkCapacity: decimal?` |
| Xe máy | `bikeType: string?`, `hasStorageBox: bool?` |
| Xe điện | `batteryCapacity: string?`, `chargingType: string?`, `chargingTimeHours: string?`, `maxRangeKm: decimal?`, `batteryType: string?` |
| Gallery | `galleryImages: string[]?` |

Không gửi trường ô tô cho xe máy hoặc ngược lại; xe gasoline không nhận trường pin/sạc. Seat count phải dương; dung tích và phạm vi không âm. Khi PUT, `detail` null/bỏ qua giữ chi tiết tương thích; gửi object detail thay các trường scalar tương ứng (trường thiếu thành null). Gallery null/bỏ qua giữ gallery; `[]` xóa gallery. Đổi loại xe xóa chi tiết không tương thích. Xe có lịch sử thuê/vận hành không được DELETE (400).

Danh sách xe hiện không tải `detail` như endpoint chi tiết. Dùng `GET /api/vehicles/{id}` khi cần gallery/thông số đầy đủ.

### Tìm xe khả dụng cho customer

`GET /api/vehicles/search` công khai, trả envelope 6 trường; `data={items,page,pageSize,totalCount,appliedWindow}`. Query `facilityId` dương giới hạn đúng cơ sở; `from`/`to` cùng có hoặc cùng bỏ, giờ UTC, `rentalType=hourly|longterm`, `hasDriver=true|false`. Mặc định `page=1&pageSize=20`, page>=1, pageSize 1..100, sai trả 400 ở HTTP/Application; trang vượt phạm vi vẫn giữ tổng sau lọc.

Từ 2026-10-23, public search chỉ hiển thị xe đã cấu hình ít nhất một hạng `allowedLicenseTypes` khác khoảng trắng. Mảng rỗng/NULL hoặc chỉ chứa NULL/khoảng trắng bị loại trước `totalCount` và phân trang, kể cả tìm xe có tài xế. Danh sách quản trị `GET /api/vehicles`, chi tiết xe và lịch sử đơn thuê vẫn giữ xe chưa cấu hình để có thể chỉnh sửa/tra cứu. Endpoint GPS `tracking/vehicles/nearby` là danh sách vận hành riêng, giữ nguyên.

Bộ lọc danh mục `/find`: `category=car|electric_car|motorbike|electric_motorbike`, `brand`, `seatCount` (số chỗ tối thiểu), `transmission=automatic|manual|cvt` (automatic gồm CVT), `fuelType=petrol|gasoline|electric` (tương thích Xăng/Điện), `minPrice`, `maxPrice` theo hình thức thuê, `q` tìm hãng + mẫu, năm và chi nhánh. `sort=recommended|price_asc|price_desc|newest|name_asc`, có id làm thứ tự phụ ổn định. Tất cả áp dụng trước đếm tổng/phân trang. `vehicleType` trong kết quả thêm `type=car|motorbike` để giao diện nhận diện phân khúc độc lập tên hiển thị.

Khi `hasDriver=true` và chọn cơ sở, cần ít nhất một tài xế thuộc chính cơ sở đó, tài khoản active, GPLX có ngày hết hạn hợp lệ đến cuối thời gian thuê, không có assignment `assigned` trùng giờ kể cả đệm `Assignment:DriverBufferMinutes` (mặc định 60). Không lấy tài xế từ cơ sở khác. Xe vẫn phải trống lịch và không bị bảo dưỡng chặn. Tìm kiếm chưa gán/giữ chỗ tài xế; việc phân công vẫn ở luồng vận hành. Tự lái không cần tài xế rảnh nhưng giữ kiểm tra xe/GPLX khách. Cả hourly/longterm hỗ trợ hasDriver. Public search chỉ trả xe available, không có rental đang renting/post_inspection, dù chọn lịch tương lai; áp dụng trước totalCount/pagination. Admin catalogue giữ toàn bộ đội xe.

Ví dụ `/api/vehicles/search?facilityId=5&hasDriver=true&rentalType=hourly&from=2027-01-10T01:00:00Z&to=2027-01-10T03:00:00Z&page=1&pageSize=20`.

## 8. Chính sách và thuê xe

| Method | Endpoint | Quyền | Body | HTTP / data |
| --- | --- | --- | --- | --- |
| GET | `/api/rental-policies` | Management | Query `page=1`, `pageSize=20` | 200 / `RentalPolicyPage` |
| POST | `/api/rental-policies` | Management | `PolicyRequest` | 201 / `RentalPolicy` |
| PATCH | `/api/rental-policies/{id}/status` | Management (Admin, Manager) | `PolicyStatusRequest` | 200 / `RentalPolicy` |
| POST | `/api/rentals/hourly` | Customer | `RentalRequest` | 201 / `Rental` |
| POST | `/api/rentals/longterm` | Customer | `RentalRequest` | 201 / `Rental` |
| GET | `/api/rentals/{id}` | RentalRead + phạm vi | Không | 200 / `Rental` |
| GET | `/api/rentals` | CustomerService | Query status/customerId/vehicleId/from/to/page/pageSize | 200 / `PagedResult<OperationalRentalSummary>`; mỗi item có `rentalType` (`hourly`/`longterm`) từ DB, khớp detail |
| GET | `/api/rentals/{id}/detail` | CustomerService | Không | 200 / `OperationalRentalDetail` |
| GET | `/api/rentals/mine` | Customer | Query tab/page/pageSize | 200 / `PagedResult<CustomerRentalSummary>` |
| GET | `/api/rentals/mine/{id}` | Customer, đơn của mình | Không | 200 / `CustomerRentalDetail` |
| PUT | `/api/rentals/{id}` | CustomerService | `UpdateRentalRequest` | 200 / `Rental` |
| POST | `/api/rentals/{id}/confirm` | CustomerService | Không | 200 / `RentalLifecycleResult` |
| POST | `/api/rentals/{id}/start` | CustomerService | Không | 200 / `RentalLifecycleResult` |
| POST | `/api/rentals/{id}/return` | CustomerService | `{ "returnedAt": "UTC" }` hoặc null | 200 / `RentalLifecycleResult` |
| POST | `/api/rentals/{id}/complete` | CustomerService | Không | 200 / `RentalLifecycleResult` |
| POST | `/api/rentals/{id}/cancel` | Customer chủ đơn / CustomerService | reason/initiator?/waiveFee? | 200 / `RentalLifecycleResult` |
| GET | `/api/handovers` | CustomerService | Query status/customerId/vehicleId/from/to/page/pageSize | 200 / `PagedResult<HandoverSummary>` |
| GET | `/api/handovers/{rentalId}` | CustomerService | Không | 200 / `HandoverDetail` |
| POST | `/api/handovers/{rentalId}/confirm` | CustomerService | Không | 200 / `HandoverConfirmation` |
| PUT | `/api/rentals/{id}/contract` | CustomerService | `RentalContractRequest` | 200 / `Rental` |
| POST | `/api/rentals/{id}/assignments` | Management | `AssignmentRequest` | 200 / null |
| POST | `/api/rentals/{id}/inspections/pre` | Inspection | `InspectionRequest` | 200 / `Inspection` |
| POST | `/api/rentals/{id}/inspections/post` | Inspection | `InspectionRequest` | 200 / `Inspection` |
| POST | `/api/rentals/{id}/status/{status}` | CustomerService | Không | 200 / `Rental` |

### Body và quy tắc

`PolicyRequest`: `vehicleTypeId: int?` null hoặc ID dương còn tồn tại, `name: string` bắt buộc, `vatRate: decimal` 0–100, `overtimeFeeRate: decimal` 0–100, `depositPercentage: decimal` 0–100, `isActive: bool = true`. Không cho tạo khi có policy active cùng phạm vi loại xe (null là phạm vi chung riêng). Lỗi tham chiếu/trùng active trả 400 với `error.errors.VehicleTypeId`; phải tạm ngưng bản cũ trước. Kiểm tra lại dưới khóa/transaction tại repository.

Danh sách validate `page>=1`, `pageSize` 1–100 tại HTTP/Application, thứ tự ID giảm dần, trả `data={items,page,pageSize,totalCount}`. Mỗi `RentalPolicy` có `rentalCount: int64`: COUNT các đơn có `policy_id` đúng bản đó, gồm mọi trạng thái và loại thuê; chưa dùng = 0. Không suy ra theo loại xe hoặc cộng đơn có policy null. Trang vượt phạm vi trả items rỗng, giữ đúng tổng. POST có count 0; PATCH giữ số đếm hiện tại.

```json
{ "vehicleTypeId": 1, "name": "Chính sách tiêu chuẩn", "vatRate": 10, "overtimeFeeRate": 20, "depositPercentage": 30, "isActive": true }
```

`PolicyStatusRequest`: `{ "isActive": true }` để kích hoạt, `{ "isActive": false }` để tạm ngưng. `id` dương; `isActive` bắt buộc là boolean, không nhận null/string/number. Thiếu/sai dữ liệu trả 400, không tồn tại trả 404, chưa đăng nhập 401, sai quyền 403. Response thành công 200 có policy trong `data` và message `Rental policy status updated successfully.`; success/error đều theo envelope 6 trường.

Chỉ đổi trạng thái bản ghi đích, không đổi điều khoản, không tạo phiên bản, không tự ngừng bản khác và không thay giá rental/invoice cũ. Lặp cùng trạng thái vẫn 200. Kích hoạt bản inactive bị chặn 400 nếu có bản khác active cùng phạm vi. Dữ liệu trùng có sẵn vẫn chọn policy active mới nhất theo loại xe, rồi fallback policy chung. Xem FR-011 và ADR-006.

`RentalRequest`:

```json
{
  "vehicleId": 1,
  "policyId": null,
  "startTime": "2026-10-10T02:00:00Z",
  "endTime": "2026-10-10T05:00:00Z",
  "subscriptionId": null,
  "hasDriver": false,
  "paymentMethod": "payos"
}
```

- `vehicleId` dương; `policyId`/`subscriptionId` nullable; `hasDriver` mặc định false và được lưu đúng cho cả hourly/longterm. Xe phải available, không có đơn đang renting/post_inspection; API tạo đơn kiểm tra lại dưới vehicle lock.
- `paymentMethod` là `payos` (mặc định) hoặc `cash`, lưu cho hóa đơn thuê trực tiếp. Customer gửi yêu cầu giữ chỗ xe, chưa phải đơn thuê chính thức; `Rental.id` là mã yêu cầu/vận hành, `orderId` nullable là mã order riêng. Staff duyệt trước thanh toán; PayOS webhook hợp lệ hoặc staff xác nhận thực nhận tiền mặt mới phát hành order paid. Gói đã trả trước phát hành order khi staff duyệt. Xem [FR-036](../specs/FR-036-booking-request-paid-order.md).
- `endTime > startTime`; xe phải khả dụng, an toàn và không có lịch thuê hoạt động chồng nhau.
- Thuê trực tiếp tự lái (`subscriptionId=null`, `hasDriver=false`) cần GPLX customer đã approved; đơn có tài xế không yêu cầu GPLX customer. Server tự tạo invoice trong transaction tạo rental. Đơn có tài xế được mặc định đạt kiểm tra trước thuê, ghi confirmation_source=driver_default, confirmed_by=system, không giả phân công tài xế hoặc số đo/ảnh.
- Có `subscriptionId` thì server suy ra `isSubscription=true`, kiểm tra gói active thuộc customer và trừ số giờ thuê nếu gói có giới hạn giờ. Gói này không tạo invoice thuê xe.
- Giá theo giờ = đơn giá × số giờ. Giá dài hạn = đơn giá tháng × `max(1, ceil(số giờ / 720))`.
- Với policy active: VAT và deposit tính theo phần trăm tiền thuê. `Rental.totalPrice` hiện là tiền thuê + VAT; invoice.amount cho thuê trực tiếp là tiền thuê + VAT + deposit.
- Giá đơn vị được snapshot lúc đặt; thay giá xe sau đó không sửa snapshot của rental.

`RentalContractRequest`:

```json
{ "contractLink": "https://example.com/contracts/rental-42.pdf", "signedAt": "2026-10-07T02:00:00Z" }
```

Chỉ longterm ở pending/pre_inspection. URL HTTP(S), tối đa 255; ngày ký có giá trị và không ở tương lai. Đây là lưu đường dẫn chứng từ, không upload tài liệu. Hợp đồng đã ký không thay thế được; gửi lại cùng chứng từ được xử lý idempotent.

`AssignmentRequest`: `{ "driverStaffId": 3 }`. ID nullable; null được repository dùng để bỏ assignment nếu trạng thái/loại thuê cho phép. Tài xế cần active, role Driver, giấy phép hợp lệ, ca trực bao phủ chuyến và không trùng lịch. Rental phải qua pre-inspection đạt trước phân công.

`InspectionRequest`:

```json
{ "staffId": null, "isOk": true, "odometerKm": 12000, "fuelBatteryLevel": 80, "damageNotes": null, "imageUrls": "https://example.com/inspection.jpg" }
```

`staffId` có thể bỏ qua/null; server lấy danh tính từ claim. Nếu gửi ID khác claim thì 403. `odometerKm >= 0`, fuel/battery 0–100, damageNotes tối đa 500. `imageUrls` là **string**, không phải JSON array. Pre-inspection ghi khi rental pending hoặc pre_inspection; post-inspection ghi khi renting hoặc post_inspection; không ghi trùng cùng loại inspection cho cùng rental/xe. Inspection fail đưa xe về `maintenance`. Policy Inspection chỉ gồm Admin/Maintenance có claim staff_id.

### Danh sách toàn bộ biên bản kiểm tra

`GET /api/inspections` — Admin/Manager/CustomerService/Maintenance. Query tùy chọn `inspectionType=pre_rental|post_rental`, `rentalId` dương; mặc định `page=1&pageSize=20`, page>=1, pageSize 1–100. Giá trị không hợp lệ trả 400 ở HTTP và Application; không clamp. `data` có items/page/pageSize/totalCount; tổng sau lọc, trước pagination; trang vượt phạm vi giữ tổng và items rỗng. Thứ tự `inspected_at DESC NULLS LAST, id DESC` ổn định.

Item: `{ id, rentalId, vehicleId, vehicleName, licensePlate, inspectionType, isOk, inspectorEmail, inspectedAt, odometerKm, fuelBatteryLevel, damageNotes, imageUrls }`. `vehicleId` và metadata/evidence có thể null nếu dữ liệu cũ thiếu; không tự gán xe hiện tại hay email giả. Bao gồm xe cũ và rental completed/cancelled. `imageUrls` là string evidence hiện có. Pending work dùng danh sách rental theo trạng thái, không được đưa vào lịch sử khi chưa ghi biên bản. Success message: `Recorded vehicle inspections retrieved successfully.`

### Email người đang bảo trì xe

`Vehicle.maintenanceTechnicianEmails` là string[] trên GET `/api/vehicles` và GET `/api/vehicles/{id}`. Chỉ authenticated Admin/Manager/Maintenance nhận email; public/customer/Driver/CustomerService nhận `[]`. SQL lấy nhân viên Maintenance active được giao phiếu open/scheduled/in_progress, hoặc completed nhưng is_safe=false; loại cancelled/completed-safe/verified, dedup và sắp xếp email. Thiếu phân công trả `[]`; không dùng email từ phiếu cũ đã kết thúc an toàn.

### Phân công kỹ thuật viên kiểm tra trước thuê

| Method | Path | Quyền | Request / response `data` |
|---|---|---|---|
| GET | `/api/rentals/{id}/inspection-assignment` | Admin, Manager, CustomerService, Maintenance | `InspectionAssignment` hoặc null khi chưa phân công cho xe hiện tại |
| PUT | `/api/rentals/{id}/inspection-assignment` | Management (Admin/Manager) | `{ "technicianStaffId": 3 }` → `InspectionAssignment` |
| POST | `/api/rentals/{id}/inspection-assignment/checked` | Admin | `{ "technicianStaffId": 3 }` → `InspectionAssignment`; lưu phân công và kết quả kiểm tra trước thuê đạt trong cùng transaction |

POST checked chỉ cho rental pending/pre_inspection, started_at null và chưa có biên bản trước thuê của xe hiện tại. Technician phải Maintenance active; validation/quy tắc lỗi giống PUT. Lưu nguồn admin_confirmation và confirmed_by từ principal, không ghi đè evidence fail/pass, không đổi trạng thái rental hoặc tự bỏ qua thanh toán/tài xế/bảo dưỡng/kiểm tra sau thuê. GET `/api/inspections` thêm confirmationSource (technician/driver_default/admin_confirmation), confirmedBy nullable để phân biệt nguồn xác nhận. Đơn có tài xế tự có kết quả đạt khi tạo/đổi xe; mặc định không tạo số km, nhiên liệu hoặc ảnh. Sửa điều khoản trước bàn giao có thể đổi về tự lái và loại xác nhận mặc định; evidence kỹ thuật/Admin vẫn bất biến.

`InspectionAssignment`: `{ rentalId, vehicleId, technicianStaffId, technicianName, assignedAt, assignedBy, technicianEmail }`. `technicianEmail` lấy từ tài khoản thật, cho phép người đọc không có quyền staff directory hiển thị đúng người nhận việc. `id` và `technicianStaffId` phải dương; staff phải có specialty Maintenance và tài khoản active. Lưu phân công cho rental/xe hiện tại ở pending/pre_inspection, started_at null, trước khi có pre-rental evidence; có thể đổi người trước evidence. Thiếu rental trả 404; technician không hợp lệ trả 400 `TECHNICIAN_NOT_ELIGIBLE`; trạng thái không cho phép trả 409 `INSPECTION_ASSIGNMENT_NOT_ALLOWED`; đã có evidence trả 409 `INSPECTION_ALREADY_RECORDED`. `assignedBy` lấy từ email principal, không nhận từ body. Phân công không ghi evidence, không đổi trạng thái, không thay quyền Inspection hay giả danh người thực hiện. Thay xe không kế thừa phân công của xe cũ. Danh sách ứng viên dùng `/api/staff` phân trang rồi lọc `isActive`/`roles` có Maintenance ở client (không có query staffRole).

GET `/api/rentals/{id}` (flat `Rental`, đang dùng trong Admin) và `/api/rentals/{id}/detail` (nested `OperationalRentalDetail`) có thêm **boolean** `hasPassingPreInspection`, `hasPostInspection`. Hai cờ chỉ tính evidence gắn đúng rental và **vehicle_id hiện tại**. Pre phải is_ok=true, post chỉ yêu cầu đã có evidence (is_ok=false vẫn chuyển quy trình quyết toán kèm damage review theo logic hiện tại). Kết quả kiểm tra xe cũ không mở khóa bàn giao/hoàn tất; server tiếp tục kiểm tra lại khi mutation. API mới dùng envelope 6 trường chung, Swagger công bố request/response/auth và các mã 400/401/403/404/409.

### Vòng đời hiện tại

2026-10-22: Admin và customer chủ đơn được hủy ở pending/pre_inspection, trước renting, kể cả đã qua giờ nhận dự kiến nhưng chưa bàn giao. GET cancellation-preview và POST `/api/rentals/{id}/cancel` giữ cùng chính sách phí/hoàn hiện có; reason bắt buộc, customer không được waiveFee. Mine list/detail trả canCancel theo cùng hai trạng thái; mutation kiểm tra lại current status dưới lock. Từ renting trở đi chặn hủy.

```text
pending -> pre_inspection -> renting -> post_inspection -> completed
pending -> cancelled
pre_inspection -> cancelled
pending -> expired
pre_inspection -> expired
```

FR-042: `expired` là trạng thái kết thúc, do job nền đặt khi quá `rentals.expire_at` mà chưa đủ điều kiện “đã duyệt + đã thanh toán”. Hóa đơn thuê chưa thu của đơn hết hạn bị hủy trong cùng transaction; `vehicle_bookings.is_blocking=false` nên lịch xe được giải phóng. `expire_at = created_at + Rental:Expiry:ApprovalWindowMinutes` (mặc định 60 phút); `POST /api/rentals/hourly|longterm` từ chối `startTime < now + Rental:Expiry:MinimumLeadMinutes` với 400 `RENTAL_START_TIME_TOO_SOON`. Duyệt (`confirm`/`start`) và thanh toán (checkout PayOS, xác nhận tiền mặt) trên đơn `expired` trả 409 (`ILLEGAL_TRANSITION`/`RENTAL_EXPIRED`/`INVOICE_NOT_PAYABLE`). Webhook PayOS về sau khi đơn đã hết hạn vẫn ghi sổ cái nhưng không phát hành `rental_orders` và tự tạo `payments(type='refund', status='requested')`. Danh sách `mine` có thêm tab `expired`; `CustomerRentalSummary`/`CustomerRentalDetail` có thêm `expireAt: datetime|null`; danh sách vận hành nhận `status=expired`.

Luồng mới: staff duyệt yêu cầu `/confirm` → customer thanh toán PayOS hoặc staff xác nhận đã thu tiền mặt → phát hành đơn thuê chính thức; kiểm định `/inspections/pre` đạt và phân công xe/tài xế → xác nhận `/api/handovers/{rentalId}/confirm`. Confirm không yêu cầu paid; ghi confirmed_at để mở thanh toán, legacy pre_inspection chưa có confirmed_at cần duyệt bổ sung. Xác nhận bàn giao dùng cùng logic `/start`, chuyển `pre_inspection → renting`, ghi started_at và audit trong transaction. Không cần body. Chỉ bàn giao trực tiếp khi hóa đơn thuê ban đầu paid, kiểm định của **xe hiện tại** hợp lệ. Đơn có tài xế cần assignment có tài xế; đơn tự lái cần GPLX approved/còn hạn và tự ghi nhận assignment xe không có tài xế trong cùng transaction bàn giao nếu chưa có, không cần thao tác phân tài xế riêng. Assignment đang hoạt động lệch xe hoặc có tài xế trên đơn tự lái vẫn bị từ chối. Gọi lại trả 409 ALREADY_IN_STATE. Kiểm định legacy từ pending/renting vẫn tự chuyển trạng thái; xác nhận bàn giao kiểm tra lại payment và tình trạng xe để đường legacy không bỏ qua điều kiện bắt đầu thuê. `/status/{status}` cũ deprecated; dùng endpoint lifecycle riêng cho luồng mới.

### Danh sách đơn và bàn giao

CustomerService là policy gồm **Admin, Manager, CustomerService**, không bao gồm role Staff. `GET /api/rentals` không truyền customerId trả đơn mọi khách; truyền customerId để lọc một khách. `mine` lấy khách từ tài khoản đăng nhập, tab: upcoming/ongoing/completed/cancelled/expired. List mặc định `page=1`, `pageSize=20`; page>=1, pageSize 1..100; sai trả 400 tại HTTP/Application, không tự clamp. `data={items,page,pageSize,totalCount}`, tổng sau lọc; ID giảm dần, trang vượt phạm vi trả items rỗng. Lấy hết bằng cách duyệt trang tới totalCount.

Không truyền `status` cho danh sách vận hành (hoặc `tab` cho mine) trả mọi trạng thái, gồm completed/cancelled. `OperationalRentalSummary` và `CustomerRentalSummary` có `orderId: integer|null`; `OperationalRentalDetail` và `CustomerRentalDetail` có `orderId` ở cấp detail ngoài cùng. `id`/`rental.id` luôn là mã yêu cầu đặt xe; chỉ `orderId` có giá trị khi đơn chính thức đã phát hành trong rental_orders. Không suy diễn mã đơn từ trạng thái pre_inspection hay invoice unpaid. Chi tiết vận hành có `rental.confirmedAt/startedAt/returnedAt/completedAt/cancelledAt`, `invoice.status` và `history`; chi tiết customer có `status`, `nextAction` và `timeline` để theo dõi trạng thái. Trạng thái enum tại `detail.rental.status` giữ contract JSON hiện tại (0=pending, 1=pre_inspection, 2=renting, 3=post_inspection, 4=completed, 5=cancelled); status của list/customer detail là chuỗi snake_case.

`GET /api/handovers?status=all` trả tất cả đơn chờ/đã bàn giao. Status mặc định `pending` tương ứng rental pre_inspection; `completed` là đã có started_at và rental ở renting/post_inspection/completed. Lịch sử bàn giao không biến mất khi trả xe. Mỗi item: `{ rental: OperationalRentalSummary, status: "pending|completed" }`; detail: `{ rental: OperationalRentalDetail, status }` gồm khách/xe/kiểm định/audit. ID bàn giao là rentalId. Response confirm: `{ rentalId, status: "completed", rentalStatus: "renting" }`; message `Vehicle handover confirmed successfully.` trong envelope 6 trường.

### Chỉ sửa đơn trước khi thuê

`PUT /api/rentals/{id}` yêu cầu đủ bốn trường:

```json
{ "vehicleId": 1, "startTime": "2026-11-01T02:00:00Z", "endTime": "2026-11-01T06:00:00Z", "hasDriver": false }
```

- Chỉ pending/pre_inspection và started_at null; renting/post_inspection/completed/cancelled trả **409 RENTAL_NOT_EDITABLE**, kể cả gửi lại dữ liệu cũ. ID/xe dương, UTC, endTime>startTime; lỗi dữ liệu trả 400, không tồn tại trả 404.
- Giữ customer/type/subscription/policy. Không nhận giá từ client. Cùng xe giữ đơn giá snapshot; đổi xe lấy đơn giá xe mới, kiểm tra tương thích loại xe của policy. Giá/VAT/cọc tính lại theo snapshot tỷ lệ của đơn cũ; invoice unpaid đồng bộ trong transaction.
- Thay đổi thực tế bị chặn nếu có inspection, assignment active, hợp đồng đã ký, payment attempt, invoice khác unpaid hoặc đã bắt đầu checkout PayOS. Checkout ghi `invoices.checkout_started_at` trước gọi provider, kể cả provider lỗi vẫn giữ khóa sửa (link ngoài có thể đã tồn tại); không tự mở lại bằng redirect/cancel. Cần migration `0020_invoice_checkout_reservation.sql`. Không sửa chứng từ/giao dịch đã cam kết. Xe phải khả dụng, không chồng lịch hoặc maintenance; tự lái phải có GPLX hợp lệ. Longterm không hỗ trợ tài xế.
- Subscription chỉ cho dịch lịch giữ xe và thời lượng trong hạn gói, không trừ lại giờ; đổi xe/thời lượng cần đặt lại. Booking, rental detail và invoice rollback cùng nhau khi lỗi.
- Thành công trả 200 `Rental` trong envelope, message `Rental updated successfully.`; các xung đột nghiệp vụ trả 409 với mã trong `error.code`.

Nâng cấp dữ liệu cũ: migration 0020 khóa sửa các invoice unpaid đã tồn tại trước khi thêm marker, vì bản cũ không ghi nhận đã phát hành link PayOS hay chưa. Đơn mới sau migration sửa được trước checkout theo các điều kiện trên. Không tự xóa marker của dữ liệu cũ khi chưa đối soát link ngoài.

## 9. Gói thuê và gói của khách hàng

| Method | Endpoint | Quyền | Body / query | HTTP / data |
| --- | --- | --- | --- | --- |
| GET | `/api/subscription-plans` | Public | `all: bool = false` | 200 / `SubscriptionPlan[]` |
| GET | `/api/subscription-plans/{id}` | Public | Không | 200 / `SubscriptionPlan` |
| POST | `/api/subscription-plans` | Management | `PlanRequest` | 201 / `SubscriptionPlan` |
| PUT | `/api/subscription-plans/{id}` | Management | `PlanRequest` | 200 / `SubscriptionPlan` |
| DELETE | `/api/subscription-plans/{id}` | Admin | Không | 200 / null |
| GET | `/api/customer-subscriptions` | Customer | Không | 200 / `CustomerSubscription[]` |
| POST | `/api/subscription-plans/{planId}/purchase` | Customer | Không | 200 / `CheckoutResult` |
| GET | `/api/subscription-plans/purchases/{orderCode}` | Customer, giao dịch của mình | Không | 200 / `PlanPurchaseStatus` |

`all=true` chỉ có hiệu lực với Admin/Manager; người khác chỉ xem danh sách active. Endpoint GET từng ID hiện không lọc `isActive`. DELETE plan đã được dùng sẽ chuyển inactive; plan chưa dùng được xóa.

`PlanRequest`:

```json
{ "name": "Gói 30 ngày", "description": "20 giờ thuê", "price": 1500000, "durationDays": 30, "includedHours": 20, "isActive": true }
```

Name bắt buộc; description nullable; price >= 0.01; durationDays 1–3650; includedHours nullable hoặc >= 0; isActive mặc định true. `includedHours=null` nghĩa là không giới hạn giờ theo kiểm tra trừ giờ hiện tại.

Luồng mua: tạo checkout → mở `checkoutUrl` → PayOS webhook xác nhận → BE tạo/gia hạn subscription → frontend đọc trạng thái purchase và gọi `/api/customer-subscriptions`. Redirect về frontend chưa đủ để coi là đã trả tiền. Mã order của mua gói dùng vùng từ `1_000_000_000` trở lên.

## 10. Hóa đơn, thanh toán và hoàn tiền

| Method | Endpoint | Quyền | Body / query | HTTP / data |
| --- | --- | --- | --- | --- |
| GET | `/api/invoices` | FinancialRead + phạm vi | Không | 200 / `InvoiceSummary[]` |
| GET | `/api/invoices/{invoiceId}` | FinancialRead + phạm vi | Không | 200 / `InvoiceDetail` |
| POST | `/api/rentals/{rentalId}/invoice` | CustomerService | Không | 201 / `InvoiceSummary` |
| POST | `/api/invoices/{invoiceId}/checkout` | Customer, invoice của mình | Không | 200 / `CheckoutResult` |
| POST | `/api/invoices/{invoiceId}/cash-confirmation` | CustomerService (Admin/Manager/CustomerService) | Không | 200 / `PaymentResult` |
| GET | `/api/payments` | FinancialRead + phạm vi | `invoiceId: int?` | 200 / `PaymentSummary[]` |
| POST | `/api/payments/checkouts/{orderCode}/cancel` | Customer, giao dịch của mình | Không | 200 / `CheckoutCancellation` |
| POST | `/api/payments/webhooks/payos` | Public, xác minh chữ ký | `PayOsWebhookRequest` | 200 / null |
| POST | `/api/payments/{paymentId}/refunds` | CustomerService | `RefundRequest` | 202 / `PaymentResult` |
| POST | `/api/payments/refunds/{refundId}/approve` | Admin | Không | 200 / `PaymentSummary` |
| POST | `/api/payments/refunds/{refundId}/reject` | Admin | Không | 200 / `PaymentSummary` |

Tạo invoice chỉ dành cho rental trực tiếp; rental dùng subscription bị từ chối 400. Rental trực tiếp đã tự tạo invoice khi đặt; dùng GET để tìm invoice thay vì mặc định phải tạo thêm. Repository trả invoice hiện có khi đã tồn tại.

Invoice detail trả `canPay` boolean và `paymentBlockedReason` nullable; trước duyệt là false/RENTAL_CONFIRMATION_REQUIRED. Checkout chỉ dành cho invoice chọn payos, unpaid, rental chưa hủy và đã confirmed_at; sai phương thức trả 409 PAYMENT_METHOD_MISMATCH. Invoice overtime không cần duyệt lại. Checkout invoice đã paid trả 409; checkout/purchase trả 503 khi PayOS chưa cấu hình.

Cash-confirmation validate invoiceId dương; lấy đúng amount từ server, ghi người thu/thời gian, không nhận amount hoặc trạng thái do customer gửi. Chưa duyệt/sai phương thức/hủy trả 409, role không được phép trả 403, chưa đăng nhập 401. Gọi lặp trả payment/order đã tồn tại. Webhook PayOS kiểm tra chữ ký, số tiền và provider reference; callback lặp/đồng thời không tạo trùng. Ghi paid ledger, invoice paid và `rental_orders` trong cùng transaction. Không tự tạo order khi mở checkout, chọn phương thức hoặc duyệt yêu cầu chưa trả tiền. Tiền thực nhận từ link legacy trước duyệt vẫn vào ledger, chờ duyệt mới phát hành order. Overtime không tạo order thứ hai. Redirect/status=PAID không xác nhận thanh toán.

Cần migration `0021_rental_orders_and_cash_payment.sql`: thêm invoices.payment_method và rental_orders (mã order riêng, unique rental_id/payment_id, amount/method/payment_status/confirmed_by/created_at); hosted migrator tự đọc file. Backfill yêu cầu có confirmed_at và bằng chứng paid ledger hoặc gói trả trước, bỏ yêu cầu hủy.

Cancel checkout hỏi lại trạng thái PayOS: chỉ chốt cancelled nếu link đã `CANCELLED`/`EXPIRED`; `PAID` hoặc còn pending trả 409; không tra được/không thuộc customer trả 404 theo action/repository. Không tin query param frontend để tự kết luận đã hủy. PayOS không gửi webhook khi khách hủy link; frontend gọi cancel endpoint sau khi về cancelUrl.

`RefundRequest`: `{ "amount": 100000 }`, amount >= 1, không vượt tổng tiền đã thanh toán còn có thể hoàn. Request lưu trạng thái `requested`, approve/reject dành cho Admin. **Luồng hiện tại ghi nhận/duyệt hoàn tiền trong DB; chưa có worker payout gửi tiền hoàn thật qua PayOS.**

Ví dụ webhook để hiểu cấu trúc (chữ ký minh họa không hợp lệ để gửi thật):

```json
{
  "code": "00",
  "success": true,
  "data": {
    "orderCode": 123,
    "amount": 495000,
    "code": "00",
    "reference": "provider-reference",
    "paymentLinkId": "provider-link-id"
  },
  "signature": "<chu-ky-do-payos-tao>"
}
```

Data hỗ trợ trường mở rộng của provider và đưa chúng vào kiểm tra chữ ký. Signature bắt buộc; chữ ký sai trả 400. Chỉ xử lý thanh toán thành công khi `code="00"`, `success=true`, `data.code="00"`. Webhook là tích hợp server với PayOS, không phải thao tác frontend dùng để tự xác nhận payment.

## 11. Ca trực, sự cố và thay xe

| Method | Endpoint | Quyền | Body / query | HTTP / data |
| --- | --- | --- | --- | --- |
| GET | `/api/drivers/schedule` | DriverSchedule | `from`, `to`, `driverStaffId?` | 200 / `DriverScheduleEntry[]` |
| GET | `/api/drivers/shifts` | DriverSchedule | `from`, `to`, `driverStaffId?` | 200 / `DriverShift[]` |
| POST | `/api/drivers/shifts` | Management | `DriverShiftRequest` | 201 / `DriverShift` |
| PUT | `/api/drivers/shifts/{id}` | Management | `DriverShiftRequest` | 200 / `DriverShift` |
| POST | `/api/rentals/{id}/reports` | ReportStaff + `staff_id` | `IncidentReportRequest` | 201 / `IncidentReport` |
| GET | `/api/reports` | ReportStaff | `rentalId: int?` | 200 / `IncidentReport[]` |
| POST | `/api/reports/{id}/maintenance` | Management | `ReportMaintenanceRequest` | 201 / `Maintenance` |
| POST | `/api/reports/{id}/resolve` | Management | Không | 200 / null |
| POST | `/api/rentals/{id}/replacement-inspections/{vehicleId}` | Inspection | `InspectionRequest` | 200 / `Inspection` |
| POST | `/api/rentals/{id}/replacements` | Management | `VehicleReplacementRequest` | 200 / `Rental` |

`from`/`to` là datetime, `to > from`. Driver chỉ được xem lịch/ca của chính mình; nếu gửi `driverStaffId` của người khác thì 403. Quản lý có thể bỏ driverStaffId để xem tất cả. Schedule là lịch chuyến được phân công; shifts là ca trực.

| Schema | Trường JSON |
| --- | --- |
| `DriverShiftRequest` | `driverStaffId: int > 0`, `startTime: datetime`, `endTime: datetime`, `cancelled: bool = false` |
| `IncidentReportRequest` | `type: "damage" hoặc "breakdown"`, `description: string` 1–4000, `imageUrls: string?` tối đa 8000 |
| `ReportMaintenanceRequest` | `assignedStaffId: int > 0`, `facilityId: int? > 0`, `scheduledDate: date` |
| `VehicleReplacementRequest` | `vehicleId: int > 0`, `driverStaffId: int? > 0`, `reportId: int? > 0` |

- POST shift tạo ca chưa cancelled; `cancelled` có hiệu lực khi PUT. Ca trực không được chồng nhau; sửa/hủy ca phải giữ bao phủ chuyến đang được phân công.
- Admin/Manager xem tất cả report; các nhân viên khác xem report của mình. Tạo report luôn cần `staff_id`; Driver chỉ report chuyến được phân công.
- Breakdown chỉ cho rental đang renting; không report rental cancelled. Inspection và report là hai thao tác riêng.
- Manager giao nhân viên Maintenance active xử lý report. Resolve trực tiếp dành cho damage đủ điều kiện; breakdown được đóng qua recovery/thay xe.
- Xe thay phải khác xe hiện tại, cùng `vehicleTypeId` và đúng số chỗ, còn trống/an toàn, đã qua inspection đạt; giữ giá rental gốc.
- Thay xe khi đang renting cần breakdown report, giao maintenance cho xe cũ và tài xế thay khác tài xế cũ. Trước renting không gửi breakdown reportId.

Ví dụ report:

```json
{ "type": "breakdown", "description": "Xe không khởi động được giữa chuyến.", "imageUrls": "https://example.com/breakdown.jpg" }
```

## 12. Bảo trì, đánh giá và dashboard

| Method | Endpoint | Quyền | Body / query | HTTP / data |
| --- | --- | --- | --- | --- |
| GET | `/api/maintenance` | Maintenance | `vehicleId: int?`, `status: string?` | 200 / `Maintenance[]` |
| POST | `/api/maintenance` | Management | `MaintenanceRequest` | 201 / `Maintenance` |
| PUT | `/api/maintenance/{id}` | Maintenance | `MaintenanceRequest` | 200 / `Maintenance` |
| POST | `/api/rentals/{rentalId}/ratings` | Customer, rental của mình | `RateStaffRequest` | 201 / `StaffRating` |
| GET | `/api/staff/{staffId}/ratings` | Management | Không | 200 / `StaffRating[]` |
| GET | `/api/dashboard` | Management | `from: datetime?`, `to: datetime?` | 200 / `Dashboard` |

`MaintenanceRequest`:

```json
{
  "vehicleId": 1,
  "facilityId": 1,
  "assignedStaffId": 4,
  "status": "scheduled",
  "maintenanceType": "repair",
  "scheduledDate": "2026-10-10",
  "isSafe": false,
  "description": "Kiểm tra hệ thống phanh",
  "cost": 300000
}
```

VehicleId bắt buộc dương; các trường còn lại có kiểu nullable trong DTO. Tuy nhiên repository yêu cầu status thuộc `open`, `scheduled`, `in_progress`, `completed`, `cancelled`; cost không âm, completed cần `isSafe=true`. Assigned staff nếu có phải là Maintenance active. Giao maintenance qua `/api/reports/{id}/maintenance` cần open staff report; POST `/api/maintenance` tạo bản ghi trực tiếp không có điều kiện report này. Technician chỉ được PUT bản ghi được giao và không tự đổi người được giao; Admin/Manager được quản lý. PUT không được đổi vehicle của maintenance.

`RateStaffRequest`: `{ "staffId": 3, "rating": 5, "comment": "Tài xế hỗ trợ tốt." }`; staffId dương, rating là decimal từ 1–5, comment nullable tối đa 500. Rental phải thuộc customer và completed; staff phải có assignment/inspection liên quan. Gửi lại cùng rental/staff cập nhật đánh giá hiện có.

Dashboard trả `revenue`, `renting`, `maintenance`, `activeSubscriptions`. `from`/`to` chỉ lọc revenue theo `issued_at` của invoice paid; các số đếm còn lại là tình trạng hiện tại.

## 13. Trạng thái dịch vụ

| Method | Endpoint | Quyền | Thành công |
| --- | --- | --- | --- |
| GET | `/` | Public | 302: non-production sang `/swagger`, production sang `/health` |
| GET | `/health` | Public | 200 envelope, data `{ "status": "Healthy" }`; DB không sẵn sàng trả 503 |
| GET | `/Home` | Public | 302 sang `/swagger` |
| GET | `/Home/status` | Public | 200 envelope, data `ServiceStatus` |

`/health` kiểm tra mở kết nối PostgreSQL; không phải kiểm tra toàn bộ Redis/email/PayOS. `/Home/status` chỉ báo tiến trình API đang trả response. `/Home` vẫn redirect Swagger ngay cả Production, nơi Swagger không được bật.

## 14. Định vị GPS, Bản đồ và Cảnh báo

### API nhận tọa độ và vị trí xe

| Method | Endpoint | Quyền | Body / query | HTTP / data |
| --- | --- | --- | --- | --- |
| POST | `/api/tracking/ingest` | Public (X-Device-Id, X-Device-Key) | `IngestPositionsRequest` | 200 / `IngestPositionsResult` |
| GET | `/api/tracking/vehicles/{vehicleId}/last-position` | Authenticated | Không | 200 / `VehicleLocationDto` |
| GET | `/api/tracking/vehicles/{vehicleId}/history` | Authenticated | `from: datetime?`, `to: datetime?`, `page: int=1`, `pageSize: int=20` | 200 / `LocationHistoryPage` |
| GET | `/api/tracking/vehicles/nearby` | Authenticated | `lat: double`, `lng: double`, `radiusMeters: double?`, `includeEta: bool=false`, `page: int=1`, `pageSize: int=20` | 200 / `NearbyVehiclePage` |
| GET | `/api/tracking/rentals/{rentalId}/route` | Authenticated | Không | 200 / `RentalRouteSummaryDto` |
| POST | `/api/tracking/rentals/{rentalId}/verify-dropoff` | Authenticated | `currentLat: double?`, `currentLng: double?` | 200 / `DropoffVerificationResult` |
| POST | `/api/tracking/retention/purge` | Admin | `olderThanDays: int?` | 200 / `PurgeRetentionResult` |
| GET | `/api/tracking/alerts` | Management | `rentalId: int?`, `vehicleId: int?`, `isResolved: bool?`, `page: int=1`, `pageSize: int=20` | 200 / `TrackingAlertPage` |
| POST | `/api/tracking/alerts/{alertId}/resolve` | Management | Không | 200 / `TrackingAlertDto` |

### Tích hợp Mapbox (Geocoding v6, Matrix v1, Directions v5)

| Method | Endpoint | Quyền | Body / query | HTTP / data |
| --- | --- | --- | --- | --- |
| GET | `/api/map/geocode` | Authenticated | `address: string` | 200 / `GeocodeResult` |
| GET | `/api/map/reverse-geocode` | Authenticated | `lat: double`, `lng: double` | 200 / `GeocodeResult` |
| GET | `/api/map/places` | Authenticated | `query: string`, `lat: double?`, `lng: double?`, `radius: double?`, `page: int=1`, `pageSize: int=20` | 200 / `PlaceSearchPage` |
| GET | `/api/map/distance-matrix` | Authenticated | `originLat: double`, `originLng: double`, `destLat: double`, `destLng: double` | 200 / `DistanceMatrixResult` |
| GET | `/api/map/directions` | Authenticated | `originLat: double`, `originLng: double`, `destLat: double`, `destLng: double` | 200 / `DirectionsResult` |

Mapbox dùng longitude,latitude trong request; DTO vẫn Lat/Lng, mét/giây và polyline precision5. `PlaceId` là ID Mapbox. `/places` tìm địa chỉ Việt Nam (không phải POI Search Box); data có `items`, `page`, `pageSize`, `totalCount`, `page>=1`, `pageSize=1..100`, default1/20, validation HTTP và Application. Upstream tối đa10 matches, tổng là số matches sau lọc radius; giữ thứ tự relevance từ provider, page vượt phạm vi trả items rỗng. lat/lng cần đủ cặp, radius cần tâm. Missing config503; upstream502/503; không tìm địa chỉ/tuyến404; không sinh dữ liệu giả. ETA mặc định profile driving, không hứa giao thông live. Nearby giữ khoảng cách Haversine, để road distance/ETA null nếu Mapbox lỗi. Verify-dropoff thiếu vị trí đích trả422. Xem [cấu hình](mapbox-setup.md).

### Quản lý thiết bị định vị (Devices)

| Method | Endpoint | Quyền | Body / query | HTTP / data |
| --- | --- | --- | --- | --- |
| GET | `/api/devices` | Management | `vehicleId: int?`, `isActive: bool?`, `page: int=1`, `pageSize: int=20` | 200 / `DevicePage` |
| POST | `/api/devices` | Management | `RegisterDeviceRequest` | 201 / `RegisterDeviceResult` |
| GET | `/api/devices/{id}` | Management | Không | 200 / `TrackingDeviceDto` |
| PUT | `/api/devices/{id}` | Management | `UpdateDeviceRequest` | 200 / `TrackingDeviceDto` |

### Vùng địa lý và Sự kiện Geofence

| Method | Endpoint | Quyền | Body / query | HTTP / data |
| --- | --- | --- | --- | --- |
| GET | `/api/geofences` | Management | Không | 200 / `GeofenceDto[]` |
| POST | `/api/geofences` | Management | `CreateGeofenceRequest` | 201 / `GeofenceDto` |
| GET | `/api/geofences/events` | Management | `vehicleId: int?`, `rentalId: int?`, `page: int=1`, `pageSize: int=20` | 200 / `GeofenceEventPage` |

## 15. Cấu trúc dữ liệu trả về

Các trường dưới đây nằm trong `data`. `T[]` là array; page có `items`, `page`, `pageSize`, `totalCount`, không có `totalPages`.

| Schema | Trường JSON |
| --- | --- |
| `AccountResponse` | `email: string`, `fullName: string`, `gender: string?`, `phone: string?`, `avatarUrl: string?`, `roles: string[]`, `staffId: int?`, `departmentId: int?`, `departmentName: string?`, `facilityId: int?` |
| `StoredImage` | `secureUrl: string`, `publicId: string` |
| `StaffPage` | `items: Staff[]`, `page: int`, `pageSize: int`, `totalCount: int` |
| `CustomerPage` | `items: CustomerListItem[]`, `page: int`, `pageSize: int`, `totalCount: int` |
| `CustomerListItem` | `id: int`, `userId: int`, `email: string`, `fullName: string?`, `phone: string?`, `avatarUrl: string?`, `isActive: bool`, `licenseStatus: string`, `loyaltyPoint: int` |
| `CustomerDetail` | `id: int`, `userId: int`, `email: string`, `fullName: string?`, `gender: string?`, `phone: string?`, `avatarUrl: string?`, `isActive: bool`, `license: { number, type, issueDate, expiryDate, imageUrl, status }`, `loyalty: { points: int }` |
| `Staff` | `id: int`, `userId: int`, `email: string`, `fullName: string`, `phone: string?`, `departmentId: int?`, `departmentName: string?`, `facilityId: int?`, `facilityName: string?`, `hiredDate: date?`, `isDriver: bool`, `licenseNumber: string?`, `licenseType: string?`, `licenseIssueDate: date?`, `licenseExpiryDate: date?`, `licenseImageUrl: string?`, `rating: decimal?`, `isActive: bool`, `staffRole: string?` |
| `OrganizationUnit` | `id: int`, `name: string`, `address: string?` |
| `CustomerLicense` | `number: string?`, `type: string?`, `issueDate: date?`, `expiryDate: date?`, `imageUrl: string?`, `status: string` |
| `VehicleType` | `id: int`, `name: string`, `fuelType: string?`, `type: string?` |
| `VehiclePage` | `items: Vehicle[]`, `page: int`, `pageSize: int`, `totalCount: int` |
| `Vehicle` | `id: int`, `vehicleTypeId: int`, `vehicleType: string` (tên), `facilityId: int?`, `facility: string?` (tên), `status: string`, `brand: string?`, `model: string?`, `color: string?`, `yearOfManufacture: int?`, `hourlyPrice: decimal?`, `longtermPrice: decimal?`, `description: string?`, `mainImageUrl: string?`, `location: string?`, `detail: VehicleDetail?`, `licensePlate: string?` |
| `VehicleDetail` | Cùng các trường `detail` trong [body xe](#body-xe) |
| `RentalPolicy` | `id: int`, `vehicleTypeId: int?`, `name: string`, `vatRate: decimal`, `overtimeFeeRate: decimal`, `depositPercentage: decimal`, `isActive: bool`, `rentalCount: long` |
| `RentalPolicyPage` | `items: RentalPolicy[]`, `page: int`, `pageSize: int`, `totalCount: int` |
| `Rental` | `id: int`, `customerId: int`, `vehicleId: int`, `rentalType: string`, `status: string`, `isSubscription: bool`, `vatPrice: decimal`, `depositPrice: decimal`, `overtimePrice: decimal`, `totalPrice: decimal`, `startTime: datetime?`, `endTime: datetime?`, `hourlyPriceSnapshot: decimal?`, `monthlyPriceSnapshot: decimal?`, `hasDriver: bool?`, `contractLink: string?`, `signedAt: datetime?` |
| `Inspection` | `id: int`, `rentalId: int`, `type: string` (`pre_rental`/`post_rental`), `isOk: bool?`, `odometerKm: decimal?`, `fuelBatteryLevel: decimal?`, `damageNotes: string?`, `imageUrls: string?`, `inspectedAt: datetime?`, `staffId: int?`, `vehicleId: int?` |
| `SubscriptionPlan` | `id: int`, `name: string`, `description: string?`, `price: decimal`, `durationDays: int`, `includedHours: decimal?`, `isActive: bool` |
| `CustomerSubscription` | `id: int`, `customerId: int`, `planId: int`, `planName: string`, `startDate: date`, `endDate: date`, `totalHours: decimal?`, `remainingHours: decimal?`, `status: string` |
| `CheckoutResult` | `orderCode: long`, `paymentLinkId: string`, `checkoutUrl: string` |
| `PlanPurchaseStatus` | `orderCode: long`, `status: string` |
| `CheckoutCancellation` | `orderCode: long`, `status: string` (`cancelled`) |
| `InvoiceSummary` | `id: int`, `rentalId: int`, `invoiceNumber: string`, `amount: decimal`, `vatAmount: decimal`, `status: string`, `issuedAt: datetime`, `dueAt: datetime?` |
| `InvoiceDetail` | Các trường InvoiceSummary + `canPay: bool`, `paymentBlockedReason: string?`, `paymentMethod: payos/cash`, `orderId: int?` |
| `PaymentResult` | `paymentId: int`, `invoiceId: int`, `amount: decimal`, `status: string`, `providerReference: string?`, `orderId: int?` |
| `PaymentSummary` | `id: int`, `rentalId: int?`, `invoiceId: int?`, `type: string?`, `amount: decimal`, `status: string`, `method: string?`, `providerReference: string?`, `paidAt: datetime?`, `createdAt: datetime` |
| `DriverScheduleEntry` | `assignmentId: int`, `rentalId: int`, `vehicleId: int`, `driverStaffId: int`, `driverName: string`, `startTime: datetime`, `endTime: datetime`, `rentalStatus: string` |
| `DriverShift` | `id: int`, `driverStaffId: int`, `startTime: datetime`, `endTime: datetime`, `status: string` |
| `IncidentReport` | `id: int`, `rentalId: int`, `vehicleId: int`, `reporterStaffId: int`, `type: string`, `description: string`, `status: string`, `maintenanceId: int?`, `imageUrls: string?` |
| `Maintenance` | `id: int`, `vehicleId: int`, `facilityId: int?`, `assignedStaffId: int?`, `status: string?`, `maintenanceType: string?`, `scheduledDate: date?`, `isSafe: bool?`, `description: string?`, `cost: decimal?` |
| `StaffRating` | `id: int`, `rentalId: int`, `staffId: int`, `rating: decimal`, `comment: string?`, `createdAt: datetime` |
| `Dashboard` | `revenue: decimal`, `renting: long`, `maintenance: long`, `activeSubscriptions: long` |
| `ServiceStatus` | `service: string`, `status: string`, `timestamp: datetime` |

Ví dụ page xe (lược bớt trường của item):

```json
{
  "code": 200,
  "status": "success",
  "message": "Vehicles retrieved successfully.",
  "is_success": true,
  "data": {
    "items": [{ "id": 1, "vehicleTypeId": 1, "status": "available", "brand": "Toyota" }],
    "page": 1,
    "pageSize": 20,
    "totalCount": 1
  },
  "error": null
}
```

## 16. Ví dụ tích hợp frontend

### Gọi qua proxy cùng origin

```javascript
async function callApi(path, options = {}) {
  const response = await fetch(path, { credentials: "include", ...options });
  const body = await response.json().catch(() => null);
  if (!response.ok) {
    throw new Error(body?.message ?? body?.error?.detail ?? `HTTP ${response.status}`);
  }
  return body?.data;
}

await callApi("/api/auth/login", {
  method: "POST",
  headers: { "Content-Type": "application/json" },
  body: JSON.stringify({
    email: "customer@example.test",
    password: "ExamplePass123!",
    rememberMe: true
  })
});

const profile = await callApi("/api/account/profile");
const vehiclePage = await callApi("/api/vehicles?page=1&pageSize=20");
console.log(profile, vehiclePage.items);
```

Khi 401 do access token hết hạn, gọi POST `/api/auth/refresh` một lần rồi thử lại request; nếu refresh thất bại thì về đăng nhập. Trong ứng dụng nhiều request song song, dùng một refresh dùng chung đang chạy.

### Upload

```javascript
const form = new FormData();
form.append("file", selectedFile);
const image = await callApi("/api/media/avatar", { method: "POST", body: form });
console.log(image.secureUrl);
```

Không tự đặt `Content-Type` khi gửi FormData; trình duyệt cần tạo multipart boundary.

### Luồng thuê trực tiếp

1. Đăng ký + verify OTP → login.
2. Customer lưu giấy phép → CustomerService duyệt approved.
3. Tìm xe trống → POST rental hourly/longterm → lấy rental ID.
4. GET invoices để lấy invoice của yêu cầu → GET invoice detail: chờ staff POST rental confirm và canPay=true. Với payos: POST checkout → mở checkoutUrl → webhook chốt paid và phát hành order. Với cash: staff thực nhận đủ tiền rồi POST cash-confirmation → paid/order. Customer đọc lại invoice/orderId từ server.
5. Nếu longterm, nhân viên lưu chứng từ hợp đồng.
6. Inspection staff ghi pre-inspection đạt; Manager phân công tài xế nếu cần, có ca trực bao phủ.
7. CustomerService chuyển sang renting.
8. Inspection staff ghi post-inspection; CustomerService chuyển completed.
9. Customer đánh giá staff liên quan.

Đây là luồng tích hợp đề xuất theo API hiện có. Các điều kiện chuyển trạng thái thực thi ở server được mô tả ở mục thuê xe; không suy ra thêm gate thanh toán/hợp đồng chưa có trong mã.

## 17. Phạm vi hiện tại và nguồn đối chiếu

- Tài liệu mô tả endpoint đã có trong mã nguồn, không biến các FR đang dự kiến thành endpoint tồn tại.
- Chưa có controller endpoint cho danh sách rental, customer tự report sự cố, loyalty, renewal hợp đồng, lịch thanh toán dài hạn hoặc tính overtime tự động.
- Không có API PUT/DELETE rental policy hiện tại; tạo policy mới để sử dụng cho các lần đặt tiếp theo.
- Các message chưa có mapping riêng dùng fallback của response filter, ví dụ refresh/delete xe/maintenance list/ratings/cancel checkout. Client nên dựa HTTP và dữ liệu, không so sánh cứng text message.
- Contract response có HTTP tests trên host kiểm thử độc lập không cần DB/Redis/provider; việc gọi BE triển khai và dịch vụ ngoài vẫn cần xác nhận riêng.

Nguồn chính:

- [Controllers](../src/VehicleRental.WebApi/Controllers/), [Program.cs](../src/VehicleRental.WebApi/Program.cs), [AuthorizationPolicies.cs](../src/VehicleRental.WebApi/Middlewares/Authentication/AuthorizationPolicies.cs).
- [Success response filter](../src/VehicleRental.WebApi/Middlewares/Responses/ApiSuccessResponseFilter.cs), [Swagger response filter](../src/VehicleRental.WebApi/Middlewares/Responses/ApiSuccessResponseOperationFilter.cs).
- [Operations schemas](../src/VehicleRental.Application/DTOs/Operations/OperationsDtos.cs), [recovery schemas](../src/VehicleRental.Application/DTOs/Operations/RecoveryDtos.cs), [payment schemas](../src/VehicleRental.Application/DTOs/Payments/PaymentDtos.cs), [staff schemas](../src/VehicleRental.Application/DTOs/Organization/StaffDtos.cs).
- [Vehicle CRUD](vehicle-crud.md), [rental contract](rental-contract-api.md), [staff authorization](staff-authorization.md), [operational recovery](operational-recovery.md), [specs](../specs/README.md).

Khi tài liệu cũ khác mã nguồn (ví dụ callback Google `/signin-google` hoặc DELETE ghi 204), ưu tiên mã nguồn và tài liệu API này ở snapshot hiện tại. Cập nhật tài liệu cùng thay đổi endpoint/DTO/policy tiếp theo.


## Hoàn thiện API/field cho frontend — 2026-10-20

Tất cả response giữ envelope 6 trường `code/status/is_success/data/message/error`. API danh sách dưới đây trả `data={items,page,pageSize,totalCount}` (dashboard thêm period/previousPeriod), mặc định page=1/pageSize=20, page>=1 và pageSize=1..100. Sai pagination trả 400 tại HTTP/Application; totalCount sau lọc và trước phân trang; trang vượt giới hạn trả items rỗng.

| Method | Path | Quyền | Input/chức năng |
|---|---|---|---|
| GET | `/api/users` | Admin | search, role, isActive, page, pageSize; roles đầy đủ và customerId/staffId nullable |
| GET | `/api/users/{id}` | Admin | Chi tiết user theo User ID; không trả password/token |
| PATCH | `/api/users/{id}/status` | Admin | `{isActive,reason}`; bắt buộc boolean và lý do |
| POST / DELETE | `/api/users/{id}/roles` | Admin | JSON `{role}`; role hợp lệ và có hồ sơ tương ứng |
| POST | `/api/users/{id}/sessions/revoke` | Admin | Thu hồi phiên của tài khoản đích |
| PATCH | `/api/ratings/{id}` | Admin/Manager | `{status,reason}`; visible/hidden/reported; lưu audit |
| POST | `/api/ratings/{id}/reply` | Admin/Manager | `{text}`; trả reply `{text,by,at}` |
| PATCH | `/api/reports/{id}/status` | Admin/Manager | `{status,reason}`; in_progress/rejected; rejected có thể tiếp nhận lại |
| GET | `/api/dashboard` | Admin/Manager | Snapshot typed: revenue/renting/maintenance/activeSubscriptions và previous |
| GET | `/api/dashboard/revenue-series` | Admin/Manager | Theo ngày UTC; bucket/dateIso/total/byGroup/previous, `rentals`/`previousRentals` (số chuyến thực tế bắt đầu, bỏ đơn hủy) và biên kỳ của từng dòng |
| GET | `/api/dashboard/top-vehicles` | Admin/Manager | vehicleId/name/category/revenue/rentals/hours/utilization/previous |
| GET | `/api/dashboard/maintenance-vs-revenue` | Admin/Manager | Chi phí bảo dưỡng hoàn thành và doanh thu mỗi xe/kỳ |
| GET | `/api/dashboard/plan-usage` | Admin/Manager | planId/name/usedHours/includedHours/activeSubscribers/usagePercentage/previous |

Dashboard nhận from/to dạng timestamp có UTC offset, facilityId, vehicleTypeId; các API list thêm page/pageSize. Khoảng nửa mở `[from,to)`, mặc định 30 ngày tới hiện tại, tối đa 366 ngày. previous là khoảng liền trước dài bằng khoảng hiện tại; snapshot có asOf/previousAsOf biểu thị thời điểm quan sát thực tế (không thể biết trạng thái tương lai). Revenue là tổng invoice paid theo issued_at như dashboard cũ, gồm các khoản invoice đã ghi nhận; không ngụ ý dòng tiền ròng sau hoàn tiền. Utilization là % giờ vận hành thực tế trong kỳ; maintenance cost lấy phiếu completed. Plan usage là giờ đặt theo đơn không hủy cộng overtime đã quyết toán; unlimited trả includedHours/usagePercentage=null. Khi lọc cơ sở/loại xe, subscriber thuộc nhóm được xác định qua đơn liên quan. Các số liệu theo cơ sở/loại xe và hạn mức gói hiện tại không có immutable snapshot lịch sử; xem [FR-030](../specs/FR-030-dashboard.md).

Thay đổi tài khoản/quyền ghi audit và tăng authorization_version trước khi thu hồi Redis sessions. Không khóa chính mình hoặc khóa/gỡ Admin hoạt động cuối cùng. Driver role đồng bộ cờ is_driver và đòi GPLX hợp lệ; specialty staff duy trì quy tắc một chuyên môn. Chi tiết: [FR-037](../specs/FR-037-user-administration.md).

Các endpoint đã có được bổ sung:

- `GET /api/reports`: phân trang, filter status/staffId/rentalId; createdAt/resolvedAt/customer/rental/maintenance/history/notes từ bản ghi thật. Lịch sử cũ chưa thu thập giữ rỗng. Resolve/assign maintenance tiếp tục dùng endpoint riêng.
- `GET /api/staff` và detail: avatarUrl/gender/address/totalTrips/activeTripId. Create/update cho nhập field hồ sơ mới; field mới bỏ trống giữ giá trị đã lưu khi cập nhật.
- `GET /api/staff/{staffId}/ratings`: Admin/Manager, phân trang; customerName/status/reply/vehicleName/licensePlate. Điểm trung bình chỉ tính rating visible.
- `GET /api/rentals/mine`: licensePlate/vehicleCategory/canCancel/nextAction/facility/subscriptionHoursUsed. nextAction tuân theo detail (WAITING_CONFIRMATION/PAYMENT_REQUIRED/PREPARING_VEHICLE/ENJOY_YOUR_TRIP/PROCESSING_RETURN/COMPLETED/CANCELLED).
- `GET /api/rentals/mine/{id}`: facility đổi từ string sang `{id,name,phone}` nullable; pickup.location giữ tên; thêm subscriptionHoursUsed. Giờ theo gói là thời lượng lịch thuê cộng overtime_hours, đơn thường null; không suy diễn lịch sử hoàn giờ chưa tồn tại.
- `GET /api/vehicles` và detail: supportsSubscription theo hourlyPrice như search; driverAvailable nullable (list có from/to thì kiểm tra tài xế tại cơ sở theo cửa sổ và buffer, không có cửa sổ trả null); features[] lưu thật, nhận thêm features trong POST/PUT. List có seatCount/transmission/batteryCapacity và detail projection; thiếu dữ liệu nguồn vẫn null.
- `GET/POST/PATCH /api/rental-policies`: vehicleTypeName/managedBy (staff ID nullable)/createdAt. POST lấy managedBy từ staff claim của người tạo; Admin không có staff profile giữ null.
- `GET /api/maintenance`: phân trang, filter vehicleId/status/from/to theo createdAt; thêm createdAt/rentalId. `POST /api/reports/{id}/maintenance` thêm maintenanceType/description tùy chọn, bỏ trống lấy từ report.
- Driver assignment response và `GET /api/drivers/schedule`: thêm vehicleName/licensePlate/bufferMinutes; schedule phân trang, Driver chỉ đọc lịch của mình.
- `GET /api/facilities`: phân trang, thêm phone; POST/PUT facilities nhận phone tối đa 50 ký tự; null khi cập nhật giữ hotline cũ, chuỗi rỗng có thể xóa hotline.

Migration mới: 0022_admin_user_audit.sql, 0023_operations_api_completion.sql, 0024_catalog_contact_fields.sql; triển khai qua migration runner. Xem [FR-038](../specs/FR-038-operations-api-completion.md), [FR-039](../specs/FR-039-frontend-api-completion.md).

## Audit logs và danh mục role — 2026-10-21

| Method | Path | Policy/quyền | Input và kết quả |
|---|---|---|---|
| GET | `/api/audit-logs` | Management: Admin, Manager | `search`, `action`, `page=1`, `pageSize=20`; trả trang nhật ký quản trị tài khoản. |
| GET | `/api/roles` | RequireAdmin | `search`, `page=1`, `pageSize=20`; trả trang danh mục role. |
| GET | `/api/roles/{id}` | RequireAdmin | Chi tiết role; ID dương, thiếu trả 404. |
| POST | `/api/roles` | RequireAdmin | JSON `{ "name": "Auditor" }`; trả 201 và header Location. |
| PUT | `/api/roles/{id}` | RequireAdmin | JSON `{ "name": "AuditViewer" }`; cập nhật role tùy chỉnh chưa gán. |
| DELETE | `/api/roles/{id}` | RequireAdmin | Xóa role tùy chỉnh chưa gán; trả 200, data=null. |

Hai danh sách trả `data={items,totalCount,page,pageSize}`; page>=1, pageSize từ 1 đến 100, mặc định 1/20. HTTP và Application cùng validate; sai trả 400. totalCount sau lọc/trước phân trang; trang vượt phạm vi trả items rỗng và giữ count. Role sắp ID giảm dần; audit sắp timestamp giảm dần rồi ID giảm dần. Mọi response giữ envelope 6 trường kể cả null; chưa đăng nhập trả 401, thiếu quyền trả 403.

Audit search tối đa 255 ký tự, tìm không phân biệt hoa thường theo email/tên/ID của người thao tác và tài khoản đích hoặc mã `AUD-{id}`; action tối đa 40 ký tự, lọc chính xác sau chuẩn hóa. Các action lịch sử `role:add`, `role:remove`, `deactivate`, `activate`, `sessions:revoke` lần lượt được trả thành `role_assigned`, `role_removed`, `account_locked`, `account_unlocked`, `session_revoked`. Action khác giữ nguyên; filter không có kết quả trả trang rỗng.

Ví dụ response audit:

```json
{
  "code": 200,
  "status": "success",
  "is_success": true,
  "data": {
    "items": [{
      "id": "AUD-1001",
      "timestamp": "2026-10-21T18:30:00Z",
      "actorId": 1,
      "actorEmail": "admin@thongdong.vn",
      "actorName": "Quản trị viên",
      "action": "role_assigned",
      "targetUserId": 42,
      "targetUser": "staff1@thongdong.vn",
      "details": "Staff",
      "ipAddress": "14.238.102.55",
      "status": "success"
    }],
    "totalCount": 1,
    "page": 1,
    "pageSize": 20
  },
  "message": "Audit logs retrieved successfully.",
  "error": null
}
```

`details` lấy reason đã lưu (với gán/gỡ role là tên role); không tự dựng lời giải thích lịch sử. Metadata người dùng bị thiếu và IP cũ giữ null. Thao tác quản trị tài khoản mới ghi remote IP theo cấu hình forwarded headers hiện có. Các mutation đã commit có status=success; API này không tạo nhật ký thất bại và không cho sửa/xóa nhật ký.

Role trả `{id,name,isSystem,userCount}`. Tên bắt buộc, trim, tối đa 255 ký tự, không chứa dấu phẩy và duy nhất không phân biệt hoa thường; trùng trả 409. Không đổi tên/xóa Admin, Manager, Staff, CustomerService, Driver, Maintenance, Customer; role tùy chỉnh đang có người dùng cũng trả 409 khi đổi tên/xóa. Gửi lại đúng tên hiện tại là idempotent. Role tùy chỉnh có thể gán/gỡ qua `/api/users/{id}/roles` và lọc bằng query role của `/api/users`; hệ thống dùng tên đã lưu và giữ nguyên quy tắc hồ sơ/chuyên môn, bảo vệ Admin cuối cùng và thu hồi phiên. Role tùy chỉnh không tự cấp quyền của policy hệ thống.

Cần áp dụng `0026_audit_log_metadata.sql` và `0027_role_catalogue.sql` trước khi dùng API mới. Migration uniqueness dừng nếu dữ liệu role cũ trùng tên không phân biệt hoa thường; không tự xóa dữ liệu. Xem [FR-037](../specs/FR-037-user-administration.md), [FR-041](../specs/FR-041-role-catalog.md).

## Thứ tự mặc định danh sách Admin — 2026-10-22

Xe: created_at DESC, id DESC. Customer/GPLX, cơ sở, vai trò và nhân viên: ID DESC trước LIMIT/OFFSET. Các bảng không có ngày tạo hồ sơ dùng ID làm thứ tự tạo. API danh mục legacy phòng ban/loại xe/gói vẫn trả đầy đủ array; FE xếp ID DESC trước chia trang. Lịch chuyến/ca giữ giờ bắt đầu tăng dần, dashboard giữ thứ tự theo chỉ số.
# Phân công/inspect sau trả xe — 2026-10-22

GET/PUT `/api/rentals/{id}/inspection-assignment` và POST `/api/rentals/{id}/inspection-assignment/checked` nhận query `inspectionType=pre_rental|post_rental` (mặc định pre_rental). POST/PUT giữ body `{technicianStaffId}`; approveRequest chỉ áp dụng trước thuê. Post chỉ cho status post_inspection, không có evidence post hiện tại; không tự hoàn tất hoặc thanh toán. GET đọc phân công độc lập theo rental/vehicle/type. PUT cần Management, POST checked chỉ Admin, kỹ thuật viên Maintenance active. Invalid type trả 400 tại HTTP/Application, sai trạng thái/evidence trả 409, missing rental 404; envelope đủ 6 trường. Xác nhận post lưu is_ok=true, confirmation_source=admin_confirmation và confirmed_by từ principal. Biên bản không đạt dùng API biên bản đầy đủ; không ghi đè. Cần migration 0029, dữ liệu phân công cũ giữ type pre_rental.


### GPLX theo từng xe và nhiều giấy phép (2026-10-23)

- Vehicle create/update/detail/list/search bổ sung `allowedLicenseTypes: string[]`: danh sách hạng bằng được chấp nhận, chuẩn hóa chữ hoa và loại trùng. Danh sách trống giữ quy tắc xác minh GPLX tương thích cho đơn hiện hữu, nhưng từ 2026-10-23 xe chưa cấu hình bị ẩn khỏi public search. Không suy diễn hạng cao hơn bao hàm hạng thấp hơn.
- `GET /api/customer/license` giữ các trường hồ sơ hiện tại và bổ sung `id`, `licenses` chứa các GPLX độc lập của khách (mỗi bằng có số, hạng, ngày cấp/hết hạn, ảnh và trạng thái riêng).
- `PUT /api/customer/license` multipart bổ sung `LicenseId` tùy chọn; JSON `/api/customer/license/url` dùng `licenseId`. Có ID: chỉ cập nhật bằng thuộc khách hiện tại; không ID: thêm hoặc cập nhật theo số GPLX. Gửi lại đặt riêng bằng đó về pending, giữ các bằng khác.
- `PUT /api/customers/{id}/license-status` nhận `{status, licenseId?}` để duyệt từng bằng. Bỏ ID giữ hành vi duyệt bằng hiện tại. Customer detail bổ sung `license.licenses` cho giao diện chọn bằng.
- Đơn tự lái cần ít nhất một bằng approved, đúng một hạng cấu hình và còn hạn đến hết thời gian thuê; kiểm tra lại ở backend khi đặt, sửa xe, phân xe và bàn giao. Bằng pending không cho xác nhận thuê nhưng giao diện báo chờ duyệt, không yêu cầu nhập lại. Đơn có tài xế không yêu cầu GPLX khách.
