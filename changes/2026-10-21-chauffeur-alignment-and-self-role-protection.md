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
- `OperationsController.cs`: `DELETE /api/vehicle-types/{id}` và `DELETE /api/subscription-plans/{id}` chuyển từ `[Authorize(Roles="Admin")]` sang policy `Management` để Manager toàn quyền CRUD danh mục đội xe.
- `NpgsqlOperationsRepository.DeleteVehicleTypeAsync`: Chặn xóa loại phương tiện đang được xe trong đội tham chiếu, trả 409 `VEHICLE_TYPE_IN_USE` thay vì để lại xe mồ côi (schema không có FK).
- `AuthorizationTests.cs`: Bổ sung ca kiểm thử `DeleteType`/`DeletePlan` theo vai trò.

## Test
- `dotnet build -warnaserror`: 0 warning, 0 error.
- `dotnet test` (AuthorizationTests, AuditLogTests, UserAdministrationTests, ApplicationServiceTests): 109/109 đạt.
- `dotnet test --filter FullyQualifiedName~UserAdministrationTests` trước đó: 15/15 đạt.
- 80/80 targeted tests đạt (AuditLog, UserAdministration, PaidOrder, OperationsCompletion, VehicleSearch, ApplicationService).

**Chưa kiểm chứng:** các integration test cần PostgreSQL cục bộ (`127.0.0.1:5432`) không chạy được trên máy này, nên hành vi xóa loại xe khi đang có xe tham chiếu chưa được test trên DB thật.
