# Tài liệu API — Car Rental System BE

Cập nhật: **10/10/2026**. Tài liệu được đối chiếu với controller, DTO, cấu hình xác thực, response filter và repository trong mã nguồn hiện tại. Các ví dụ dùng dữ liệu minh họa; thay ID, thời gian, email và URL bằng dữ liệu thật trước khi gọi.

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
14. [Cấu trúc dữ liệu trả về](#14-cấu-trúc-dữ-liệu-trả-về)
15. [Ví dụ tích hợp frontend](#15-ví-dụ-tích-hợp-frontend)
16. [Phạm vi hiện tại và nguồn đối chiếu](#16-phạm-vi-hiện-tại-và-nguồn-đối-chiếu)

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
| `ChangePasswordRequest` | `currentPassword: string` bắt buộc; `newPassword: string` 6–20 |
| `LoginRequest` | `email: string`; `password: string` 6–20; `rememberMe: bool = false` |

Giới hạn mật khẩu bao gồm cả 6 và 20 ký tự; ngoài khoảng trả 400, chi tiết trong `error.errors`, với thông báo `Mật khẩu phải có từ 6 đến 20 ký tự.` Không trim hoặc cắt ngắn mật khẩu. Tài khoản cũ có mật khẩu dài hơn 20 ký tự cần reset về giới hạn mới để đăng nhập.

OTP mặc định hết hạn sau 10 phút, cooldown gửi lại 60 giây và giới hạn kiểm tra sai theo `MaxVerificationAttempts=5`. Resend đăng ký giữ TTL còn lại, không tự kéo dài TTL. User/customer chỉ được tạo sau verify thành công. Email được chuẩn hóa lowercase. Password reset start trả cùng 202 kể cả khi email không tồn tại hoặc còn cooldown.

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
| POST | `/api/media/staff-license` | Admin | `file` | 200 / `StoredImage` |
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
| POST | `/api/staff` | Admin | `CreateStaffRequest` | 201 / `Staff` |
| PUT | `/api/staff/{id}` | Admin | `UpdateStaffRequest` | 200 / `Staff` |
| DELETE | `/api/staff/{id}` | Admin | Không | 200 / null; deactivate |
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
| GET | `/api/customers` | CustomerService | `search?`, `isActive?`, `page=1`, `pageSize=20` | 200 / `CustomerPage` |
| GET | `/api/customers/{customerId}` | CustomerService | Không | 200 / `CustomerDetail` |

Policy CustomerService gồm **Admin, Manager, CustomerService**. Danh sách tìm chuỗi theo tên/email/điện thoại, không phân biệt hoa thường; `isActive` bỏ qua thì lấy cả tài khoản active và inactive. `page >= 1`, `pageSize` từ 1 đến 100. Dữ liệu sắp xếp theo customer ID; trang vượt phạm vi trả `items=[]` và giữ `totalCount`.

`customerId` là **ID bảng customers**, khác `userId`. Chi tiết gồm liên hệ, avatar, trạng thái tài khoản, `license` và `loyalty.points`; không chứa mật khẩu/token. ID không dương trả 400, không tồn tại trả 404. Customer thông thường không được truy cập danh bạ này.

### Giấy phép của customer

| Method | Endpoint | Quyền | Body | HTTP / data |
| --- | --- | --- | --- | --- |
| GET | `/api/customer/license` | Customer | Không | 200 / `CustomerLicense` hoặc null |
| PUT | `/api/customer/license` | Customer | Multipart: `number`, `type`, `issueDate?`, `expiryDate`, `file` | 200 / `CustomerLicense` |
| PUT | `/api/customer/license/url` | Customer | JSON `LicenseRequest` | 200 / `CustomerLicense` |
| PUT | `/api/customers/{customerId}/license-status` | CustomerService | `LicenseReviewRequest` | 200 / `CustomerLicense` |

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

`number` và `type` bắt buộc, tối đa 255. `issueDate` và `imageUrl` nullable; URL phải hợp lệ. `expiryDate` không trước ngày hiện tại UTC. Lưu giấy phép đưa status về `pending`. Duyệt bằng `{ "status": "approved" }` hoặc `{ "status": "rejected" }`. `customerId` là ID hồ sơ customer.

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

## 8. Chính sách và thuê xe

| Method | Endpoint | Quyền | Body | HTTP / data |
| --- | --- | --- | --- | --- |
| GET | `/api/rental-policies` | Management | Không | 200 / `RentalPolicy[]` |
| POST | `/api/rental-policies` | Management | `PolicyRequest` | 201 / `RentalPolicy` |
| POST | `/api/rentals/hourly` | Customer | `RentalRequest` | 201 / `Rental` |
| POST | `/api/rentals/longterm` | Customer | `RentalRequest` | 201 / `Rental` |
| GET | `/api/rentals/{id}` | RentalRead + phạm vi | Không | 200 / `Rental` |
| PUT | `/api/rentals/{id}/contract` | CustomerService | `RentalContractRequest` | 200 / `Rental` |
| POST | `/api/rentals/{id}/assignments` | Management | `AssignmentRequest` | 200 / null |
| POST | `/api/rentals/{id}/inspections/pre` | Inspection | `InspectionRequest` | 200 / `Inspection` |
| POST | `/api/rentals/{id}/inspections/post` | Inspection | `InspectionRequest` | 200 / `Inspection` |
| POST | `/api/rentals/{id}/status/{status}` | CustomerService | Không | 200 / `Rental` |

### Body và quy tắc

`PolicyRequest`: `vehicleTypeId: int?`, `name: string` bắt buộc, `vatRate: decimal` 0–100, `overtimeFeeRate: decimal` 0–100, `depositPercentage: decimal` 0–100, `isActive: bool = true`.

```json
{ "vehicleTypeId": 1, "name": "Chính sách tiêu chuẩn", "vatRate": 10, "overtimeFeeRate": 20, "depositPercentage": 30, "isActive": true }
```

`RentalRequest`:

```json
{
  "vehicleId": 1,
  "policyId": null,
  "startTime": "2026-10-10T02:00:00Z",
  "endTime": "2026-10-10T05:00:00Z",
  "subscriptionId": null,
  "hasDriver": false
}
```

- `vehicleId` dương; `policyId`/`subscriptionId` nullable; `hasDriver` mặc định false. Longterm luôn truyền `hasDriver=false` ở server, dù body gửi true.
- `endTime > startTime`; xe phải khả dụng, an toàn và không có lịch thuê hoạt động chồng nhau.
- Thuê trực tiếp (`subscriptionId=null`) cần giấy phép customer đã approved; server tự tạo invoice trong transaction tạo rental.
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

`staffId` có thể bỏ qua/null; server lấy danh tính từ claim. Nếu gửi ID khác claim thì 403. `odometerKm >= 0`, fuel/battery 0–100, damageNotes tối đa 500. `imageUrls` là **string**, không phải JSON array. Pre-inspection ghi khi rental pending; post-inspection ghi khi renting; không ghi trùng cùng loại inspection cho cùng rental/xe. Inspection fail đưa xe về `maintenance`.

### Vòng đời hiện tại

```text
pending -> pre_inspection -> renting -> post_inspection -> completed
pending -> cancelled
```

Pre-inspection đạt tự đưa rental sang `pre_inspection`; post-inspection tự đưa sang `post_inspection`. Status endpoint kiểm tra transition hợp lệ, chứng từ inspection của xe hiện tại và tài xế khi cần. Chỉ hủy từ pending bằng status endpoint hiện có. Customer không được tự gọi endpoint đổi status. Không có GET danh sách rentals trong controller hiện tại.

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
| GET | `/api/invoices/{invoiceId}` | FinancialRead + phạm vi | Không | 200 / `InvoiceSummary` |
| POST | `/api/rentals/{rentalId}/invoice` | CustomerService | Không | 201 / `InvoiceSummary` |
| POST | `/api/invoices/{invoiceId}/checkout` | Customer, invoice của mình | Không | 200 / `CheckoutResult` |
| GET | `/api/payments` | FinancialRead + phạm vi | `invoiceId: int?` | 200 / `PaymentSummary[]` |
| POST | `/api/payments/checkouts/{orderCode}/cancel` | Customer, giao dịch của mình | Không | 200 / `CheckoutCancellation` |
| POST | `/api/payments/webhooks/payos` | Public, xác minh chữ ký | `PayOsWebhookRequest` | 200 / null |
| POST | `/api/payments/{paymentId}/refunds` | CustomerService | `RefundRequest` | 202 / `PaymentResult` |
| POST | `/api/payments/refunds/{refundId}/approve` | Admin | Không | 200 / `PaymentSummary` |
| POST | `/api/payments/refunds/{refundId}/reject` | Admin | Không | 200 / `PaymentSummary` |

Tạo invoice chỉ dành cho rental trực tiếp; rental dùng subscription bị từ chối 400. Rental trực tiếp đã tự tạo invoice khi đặt; dùng GET để tìm invoice thay vì mặc định phải tạo thêm. Repository trả invoice hiện có khi đã tồn tại.

Checkout invoice đã paid trả 409; checkout/purchase trả 503 khi PayOS chưa cấu hình. Sau thanh toán, server kiểm tra số tiền và provider reference khi xử lý webhook.

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

## 14. Cấu trúc dữ liệu trả về

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
| `RentalPolicy` | `id: int`, `vehicleTypeId: int?`, `name: string`, `vatRate: decimal`, `overtimeFeeRate: decimal`, `depositPercentage: decimal`, `isActive: bool` |
| `Rental` | `id: int`, `customerId: int`, `vehicleId: int`, `rentalType: string`, `status: string`, `isSubscription: bool`, `vatPrice: decimal`, `depositPrice: decimal`, `overtimePrice: decimal`, `totalPrice: decimal`, `startTime: datetime?`, `endTime: datetime?`, `hourlyPriceSnapshot: decimal?`, `monthlyPriceSnapshot: decimal?`, `hasDriver: bool?`, `contractLink: string?`, `signedAt: datetime?` |
| `Inspection` | `id: int`, `rentalId: int`, `type: string` (`pre_rental`/`post_rental`), `isOk: bool?`, `odometerKm: decimal?`, `fuelBatteryLevel: decimal?`, `damageNotes: string?`, `imageUrls: string?`, `inspectedAt: datetime?`, `staffId: int?`, `vehicleId: int?` |
| `SubscriptionPlan` | `id: int`, `name: string`, `description: string?`, `price: decimal`, `durationDays: int`, `includedHours: decimal?`, `isActive: bool` |
| `CustomerSubscription` | `id: int`, `customerId: int`, `planId: int`, `planName: string`, `startDate: date`, `endDate: date`, `totalHours: decimal?`, `remainingHours: decimal?`, `status: string` |
| `CheckoutResult` | `orderCode: long`, `paymentLinkId: string`, `checkoutUrl: string` |
| `PlanPurchaseStatus` | `orderCode: long`, `status: string` |
| `CheckoutCancellation` | `orderCode: long`, `status: string` (`cancelled`) |
| `InvoiceSummary` | `id: int`, `rentalId: int`, `invoiceNumber: string`, `amount: decimal`, `vatAmount: decimal`, `status: string`, `issuedAt: datetime`, `dueAt: datetime?` |
| `PaymentResult` | `paymentId: int`, `invoiceId: int`, `amount: decimal`, `status: string`, `providerReference: string?` |
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

## 15. Ví dụ tích hợp frontend

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
4. GET invoices để lấy invoice của rental → POST checkout → mở checkoutUrl; webhook chốt paid.
5. Nếu longterm, nhân viên lưu chứng từ hợp đồng.
6. Inspection staff ghi pre-inspection đạt; Manager phân công tài xế nếu cần, có ca trực bao phủ.
7. CustomerService chuyển sang renting.
8. Inspection staff ghi post-inspection; CustomerService chuyển completed.
9. Customer đánh giá staff liên quan.

Đây là luồng tích hợp đề xuất theo API hiện có. Các điều kiện chuyển trạng thái thực thi ở server được mô tả ở mục thuê xe; không suy ra thêm gate thanh toán/hợp đồng chưa có trong mã.

## 16. Phạm vi hiện tại và nguồn đối chiếu

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
