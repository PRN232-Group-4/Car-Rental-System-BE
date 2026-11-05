# Tách Billing, xác minh Handovers và Inspections

## Phạm vi

Phase 3.1 được lưu tại commit `c031bb1`. Mười file chuyển sang `Application/Billing`, `Domain/Billing` và `Infrastructure/Billing`. Các port, DTO và logic tài chính giữ nguyên. `PaymentsController` tách thành `InvoicesController`, `PaymentsController` và `SubscriptionPaymentsController`, lần lượt có 6, 6 và 2 action.

Giữ nguyên 14 route, HTTP verb, quyền truy cập, response và Location của tạo invoice. Đổi tám khóa message theo tên controller mới, không đổi nội dung. Bốn điều kiện mô tả Swagger cũng dùng tên controller mới. `docs/API.md` trỏ tới `BillingContracts.cs` thay cho file DTO cũ.

Phase 3.2 không cần thay đổi production code. Handovers đã có contracts, service và controller riêng. Theo [ADR-007](../docs/decisions/ADR-007-rental-editing-and-handover.md), bàn giao là góc nhìn của rental lifecycle. `HandoverService.ConfirmAsync` gọi `IRentalLifecycleService.StartAsync`; truy vấn và transaction bắt đầu thuê vẫn thuộc Lifecycle. Không thêm repository, bảng hay state machine cạnh tranh.

Phase 3.3 cũng không cần thay đổi production code. Slice này đã tồn tại từ `f42cd81` ngày 2026-10-21, trước chương trình refactor này; tám commit gần nhất không sửa file nào khớp `Inspection`. `Application/Inspections` chứa `InspectionAssignment.cs` và `InspectionDirectory.cs`, có implementation riêng trong Infrastructure và được đăng ký ở `Program.cs`.

Hai lệnh ghi bằng chứng kiểm định vẫn nằm trong Operations, và đó là quyết định đúng. `RecordInspectionAsync` lấy khóa tư vấn điều phối `724232001`, cùng khóa với repository phân công và lifecycle, rồi có thể đặt xe sang `maintenance` và chuyển trạng thái thuê. Tách riêng phần insert sẽ tạo người ghi thứ hai lên `rentals.status` trong transaction khác, đúng điều ADR-007 cấm. Bắt đầu thuê vẫn đọc lại bằng chứng trong transaction có khóa riêng, nên bất biến bàn giao không phụ thuộc repository nào đã ghi dòng đó.

Không đổi schema, dependency, bốn production project hoặc Phase0. Không chuyển `NpgsqlOperationsRepository.Recovery.cs`, không sửa FE và không deploy.

## Kiểm chứng

- Phase 3.1: Lead chạy build, đạt 0 warning và 0 error; chạy 81 test `BillingEndpointContractTests`, tất cả đạt. Worker báo toàn suite Neon đạt 1.380 test, gồm 81 ca mới. Lead không chạy lặp toàn suite đó.
- Reviewer độc lập `openai-codex/gpt-6-astra` chấp nhận snapshot Billing trước commit. So sánh với base xác nhận code thực thi của mười file được chuyển không đổi, bao gồm SQL, khóa và transaction.
- Phase 3.2: Lead chạy nhóm Handover, Rentals API, Lifecycle, Phase0 và test route trạng thái cũ trên commit `c031bb1`, đạt 50 Domain, 31 Application và 94 Integration, tổng 175 test, 0 fail.
- Phase 3.3: Lead chạy `-Filter Inspection` trên commit `89ba645`, đạt 8 Domain, 7 Application và 72 Integration, tổng 87 test, 0 fail.
- Reviewer độc lập xác nhận Handovers đã có ranh giới phù hợp ADR-007. Lượt review Inspections chạy trên `river/zai-org/GLM-5.3-Flash` vì `gpt-6-astra` hết hạn mức; model này khác họ với model của writer nhưng yếu hơn dự kiến, nên đa dạng model bị giảm.

Lệnh kiểm sau khi build:

```powershell
.\tests\run-tests-neon.ps1 -NoBuild -Filter 'Handover|FullyQualifiedName~RentalOperationsApiTests|FullyQualifiedName~RentalLifecycle|FullyQualifiedName~Phase0|FullyQualifiedName~DeprecatedRentalStatusEndpointTests'
.\tests\run-tests-neon.ps1 -NoBuild -Filter 'Inspection'
```

Các test DB dùng database dùng một lần trên Neon, không chạy schema test vào database ứng dụng.

## Giới hạn và việc còn lại

- Test HTTP dùng host kiểm thử, không chứng minh toàn bộ production startup. Bộ test đã đọc chưa gọi trực tiếp nhánh detail handover thành công, 404 và ma trận quyền của detail. Test xác nhận bàn giao qua HTTP dùng lifecycle stub; test integration riêng kiểm transaction thật và tranh chấp đồng thời.
- Chưa có test nào gọi route ghi kiểm định của kỹ thuật viên qua HTTP: `POST /api/rental/{id}/inspections/pre`, `/post` và `/replacement-inspection/{vehicleId}`. Route này là wrapper mỏng có kiểm tra policy và `staff_id`; phần repository bên dưới đã được test ở mức repository.
- Chưa so toàn bộ Swagger trước và sau Billing. Không khẳng định OpenAPI grouping giữ nguyên từng byte.
- Architecture test dùng so sánh namespace chính xác nên không còn chọn `PaymentService` và `IPaymentRepository` sau khi chuyển. Việc mở rộng coverage đã hoãn; whitelist không tăng.
- Tài liệu D2/SRS và FE cho thanh toán supplementary vẫn chưa hoàn tất. Bản ghi này không thay thế các cập nhật đó.
- Phase 3.4 Dispatch và 3.5 Maintenance chưa bắt đầu.
