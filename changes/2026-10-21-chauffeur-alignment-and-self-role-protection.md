# 2026-10-21 — Đồng bộ nghiệp vụ Chauffeur-only, tự động phân giải policy giá và chặn tự gán role
- **FR/NFR:** FR-007, FR-011, FR-012, FR-027, FR-037
- **Loại:** fix | feat

## Đã làm
- `NpgsqlOperationsRepository.CreateRentalAsync`: Tự động tìm và áp dụng chính sách giá active theo loại xe khi `policyId` không được truyền, tránh tính VAT/tiền cọc bằng 0.
- `NpgsqlOperationsRepository.CreateRentalAsync`: Bỏ điều kiện bắt buộc GPLX khách hàng đối với đơn thuê có tài xế (`hasDriver == true`).
- `NpgsqlUserAdministrationRepository.SetRoleAsync` & `UserAdministrationService.SetRoleAsync`: Chặn đứng hành vi Admin tự gán vai trò cho chính mình (`actorUserId == id`), trả về HTTP 409 Conflict.
- `DispatchController.CreateReport`: Mở quyền cho vai trò `Customer` gửi báo cáo sự cố gắn với đơn thuê mà họ sở hữu (`rentals.customer_id == user.customer_id`).
- `NpgsqlOperationsRepository.Recovery.cs`: Cho phép lưu báo cáo sự cố từ khách hàng (`reporter_staff_id = null`, `customer_id = rental.CustomerId`).
- `UserAdministrationTests.cs`: Bổ sung test kiểm thử HTTP cho hành vi chặn Admin tự gán vai trò.
- `StaffController.cs`: Mở quyền toàn diện cho vai trò `Manager` thực hiện CRUD nhân sự (`POST /api/staff`, `POST /api/staff/from-user`, `PUT /api/staff/{id}`, `DELETE /api/staff/{id}`), phân định rõ Admin quản trị tài khoản gốc và Manager toàn quyền quản lý nhân sự vận hành.
- `AuthorizationTests.cs` & `StaffIdentityTests.cs`: Cập nhật ma trận kiểm thử xác nhận vai trò `Manager` được phép tạo/sửa/xóa nhân viên.

## Test
- `dotnet test tests/VehicleRental.Tests/VehicleRental.Tests.csproj --filter "FullyQualifiedName~UserAdministrationTests"`: 15/15 tests đạt.
- 80/80 targeted tests đạt (AuditLog, UserAdministration, PaidOrder, OperationsCompletion, VehicleSearch, ApplicationService).
