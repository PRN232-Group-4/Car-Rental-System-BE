# Rà soát và gộp file — 2026-10-25

Đã gộp các khai báo nhỏ cùng tính năng trong Application. Giữ nguyên tên kiểu,
namespace, validation, chữ ký phương thức và JSON; không chuyển DTO vào controller
hay gộp service với repository.

## Các nhóm đã gộp

| Trước | Sau | Số file giảm |
|---|---|---:|
| `ICustomerService.cs` + `ICustomerService.Detail.cs` | `ICustomerService.cs`, đầy đủ hai phương thức | 1 |
| `ICustomerRepository.cs` + `ICustomerRepository.Detail.cs` | `ICustomerRepository.cs`, đầy đủ hai phương thức | 1 |
| `IAccountRepository.cs` + `AccountNotFoundException.cs` | `IAccountRepository.cs` | 1 |
| `IStaffRepository.cs` + `StaffRepositoryExceptions.cs` | `IStaffRepository.cs` | 1 |
| `AccountDtos.cs` + `AccountRequestDtos.cs` | `Accounts/AccountDtos.cs` | 1 |
| `SessionRequestDtos.cs` + `TokenDtos.cs` | `Accounts/SessionDtos.cs` | 1 |
| `CustomerListDtos.cs` + `CustomerDetailDtos.cs` | `Customers/CustomerDtos.cs` | 1 |
| `StaffDtos.cs` + `StaffRequestDtos.cs` | `Organization/StaffDtos.cs` | 1 |
| `PaymentDtos.cs` + `PaymentsRequestDtos.cs` | `Payments/PaymentDtos.cs` | 1 |

Số lượng `.cs` trong Application, không tính `bin/obj`: **129 → 120**.
Trong đó DTO: **26 → 21**, interface/contract: **45 → 41**.
Đây là số lượng riêng của đợt gộp file; các thay đổi EF Core ở Infrastructure
được thống kê riêng. Các file kết quả vẫn ngắn, cùng tính năng và có thể tìm kiểu
bằng tên qua IDE.

Trong Infrastructure đã gộp thêm **3 file**: `NpgsqlCustomerRepository.Detail.cs`
vào repository chính; `NpgsqlOperationsRepository.PolicyStatus.cs` vào Policies;
`NpgsqlOperationsRepository.Contracts.cs` vào Editing. Tổng cộng **12 file nguồn
được giảm nhờ gộp**. Hai file Persistence mới phục vụ EF giữ mapping và context
tách biệt; không tạo một file cho từng bảng.

Tổng `.cs` trong `src` sau cả chuyển EF và gộp file: **246 → 236**. Hai file test
integration mới phục vụ kiểm chứng EF; không tính vào số file mã nguồn ứng dụng.

## Các nhóm giữ riêng

- Service interface, repository interface và implementation: thuộc các trách nhiệm
  và chiều phụ thuộc khác nhau; số dòng ít không đủ lý do để gộp chúng.
- Options của các provider khác nhau: khác cấu hình, validation và vòng đời.
- Các phần của Operations repository: đã chứa nhiều nghiệp vụ và transaction;
  gộp thành một file lớn làm khó đọc hơn. Các phần lớn cần được tổ chức theo use case
  thay vì gộp cơ học.
- Response filter, Swagger filter và HTTP response helper: phục vụ ba giai đoạn
  khác nhau, có kiểm thử và liên kết tài liệu riêng.
- Test, spec và SQL migration: giữ lịch sử thay đổi, tình huống kiểm chứng và thứ tự
  migration độc lập; không gộp để giảm số file.

## Rà soát frontend

Đã kiểm kê **514 file `.ts/.tsx` trong `src`** (bao gồm test và schema sinh tự động)
và xem các nhóm API, Operations, route wrapper và feature export. Chưa sửa frontend
trong đợt này:

- Các `app/**/page.tsx` rất ngắn vẫn là entry point theo route của Next.js; không
  xóa hoặc gộp chúng với trang khác.
- `index.ts` của feature giữ public import ổn định; chỉ xóa re-export khi đã cập nhật
  toàn bộ nơi sử dụng và xác nhận không làm lẫn server/client boundary.
- `operations/api.ts` đã chứa nhiều thao tác; không gộp thêm các API chuyên biệt
  như danh sách thuê hoặc kiểm tra xe vào đó chỉ để giảm số file.
- Component biểu mẫu, validation và component UI dùng chung vẫn có trách nhiệm riêng;
  giữ riêng giúp tái sử dụng và tránh đưa toàn bộ feature vào client bundle.

## Kiểm chứng

- `dotnet build src/VehicleRental.Application/VehicleRental.Application.csproj --no-restore -warnaserror`:
  thành công, 0 warning/error.
- Không tìm thấy tham chiếu đường dẫn đến các file vừa gộp trong mã và tài liệu
  được kiểm tra. Dự án SDK tự thu thập `.cs`, không cần sửa project reference.
- `git diff --check`: không có lỗi whitespace.
- Build/test toàn solution được kiểm chứng cùng thay đổi EF Core.
