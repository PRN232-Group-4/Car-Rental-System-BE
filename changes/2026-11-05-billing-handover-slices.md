# Tách Billing và xác minh Handovers

## Phạm vi

Phase 3.1 được lưu tại commit `c031bb1`. Mười file chuyển sang `Application/Billing`, `Domain/Billing` và `Infrastructure/Billing`. Các port, DTO và logic tài chính giữ nguyên. `PaymentsController` tách thành `InvoicesController`, `PaymentsController` và `SubscriptionPaymentsController`, lần lượt có 6, 6 và 2 action.

Giữ nguyên 14 route, HTTP verb, quyền truy cập, response và Location của tạo invoice. Đổi tám khóa message theo tên controller mới, không đổi nội dung. Bốn điều kiện mô tả Swagger cũng dùng tên controller mới. `docs/API.md` trỏ tới `BillingContracts.cs` thay cho file DTO cũ.

Phase 3.2 không cần thay đổi production code. Handovers đã có contracts, service và controller riêng. Theo [ADR-007](../docs/decisions/ADR-007-rental-editing-and-handover.md), bàn giao là góc nhìn của rental lifecycle. `HandoverService.ConfirmAsync` gọi `IRentalLifecycleService.StartAsync`; truy vấn và transaction bắt đầu thuê vẫn thuộc Lifecycle. Không thêm repository, bảng hay state machine cạnh tranh.

Không đổi schema, dependency, bốn production project hoặc Phase0. Không chuyển `NpgsqlOperationsRepository.Recovery.cs`, không sửa FE và không deploy.

## Kiểm chứng

- Phase 3.1: Lead chạy build, đạt 0 warning và 0 error; chạy 81 test `BillingEndpointContractTests`, tất cả đạt. Worker báo toàn suite Neon đạt 1.380 test, gồm 81 ca mới. Lead không chạy lặp toàn suite đó.
- Reviewer độc lập `openai-codex/gpt-6-astra` chấp nhận snapshot Billing trước commit. So sánh với base xác nhận code thực thi của mười file được chuyển không đổi, bao gồm SQL, khóa và transaction.
- Phase 3.2: Lead chạy nhóm dưới đây trên commit `c031bb1`, đạt 50 Domain, 31 Application và 94 Integration, tổng 175 test, 0 fail. Nhóm này có Phase0. Architecture không có test khớp filter, không được tính là đã chạy.
- Reviewer độc lập `openai-codex/gpt-6-astra` xác nhận Handovers đã có ranh giới phù hợp với ADR-007 và chấp nhận đóng 3.2 mà không sửa production code.

Lệnh kiểm Phase 3.2 sau khi build:

```powershell
.\tests\run-tests-neon.ps1 -NoBuild -Filter 'Handover|FullyQualifiedName~RentalOperationsApiTests|FullyQualifiedName~RentalLifecycle|FullyQualifiedName~Phase0|FullyQualifiedName~DeprecatedRentalStatusEndpointTests'
```

Các test DB dùng database dùng một lần trên Neon, không chạy schema test vào database ứng dụng.

## Giới hạn và việc còn lại

- Test HTTP dùng host kiểm thử, không chứng minh toàn bộ production startup. Bộ test đã đọc chưa gọi trực tiếp nhánh detail handover thành công, 404 và ma trận quyền của detail. Test xác nhận bàn giao qua HTTP dùng lifecycle stub; test integration riêng kiểm transaction thật và tranh chấp đồng thời.
- Chưa so toàn bộ Swagger trước và sau Billing. Không khẳng định OpenAPI grouping giữ nguyên từng byte.
- Architecture test dùng so sánh namespace chính xác nên không còn chọn `PaymentService` và `IPaymentRepository` sau khi chuyển. Việc mở rộng coverage đã hoãn; whitelist không tăng.
- Tài liệu D2/SRS và FE cho thanh toán supplementary vẫn chưa hoàn tất. Bản ghi này không thay thế các cập nhật đó.
- Phase 3.3 Inspections, 3.4 Dispatch và 3.5 Maintenance chưa bắt đầu trong bước này.
