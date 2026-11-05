# Phase 2 — Quyết toán thuê một transaction và hoá đơn supplementary

- **FR/NFR:** không có mã FR riêng; thuộc quyết định D2 (giờ vượt quá số giờ gói còn lại) và bất biến "giờ gói chỉ bị trừ một lần".
- **Loại:** fix | feat | refactor

## Phạm vi

Phase 2 nằm trên nhánh `phase-2/rentals-billing`, gồm năm commit dưới đây. Trạng thái cuối của nhánh được ghi ở `248de35`. Các commit hạ tầng test nằm giữa khoảng này (`c48dab6`) không thuộc phạm vi bản ghi.

`9b2e5b4` gộp quyết toán khi hoàn tất thuê vào một transaction: khoá tư vấn theo `rentalId`, rồi `rentals FOR UPDATE`, rồi `customer_subscriptions FOR UPDATE`, sau đó mới ghi. Subscription không bao giờ bị khoá trước rental. Giờ gói bị trừ đúng một lần và chỗ trừ trùng bị xoá; `subscription_hour_ledger` có `UNIQUE(rental_id)` nên lần quyết toán thứ hai fail trên index thay vì trừ hai lần, điều mà clamp `GREATEST(0, ...)` trước đây che mất. `GetForUpdateAsync` đổi tên thành `GetAsync` vì nó chưa từng khoá. Hoàn tiền và cộng điểm loyalty vào cùng transaction; adapter loyalty không nuốt exception nữa, nên lỗi loyalty rollback cả lần hoàn tất. Thêm một guard chống hoàn tiền trùng theo rental, dùng cho cả hai đường hoàn tiền tự động. Bốn file `VehicleRental.Domain.*` chuyển sang root `Car_Rental_System_BE.*`.

`e56372b` cho loyalty đọc hạng thẻ trên connection của transaction quyết toán thay vì mở connection thứ hai. Trước đó mỗi lần quyết toán giữ hai connection, nên pool N có thể tự deadlock ở N lượt đồng thời, và hạng thẻ chỉ thấy dòng đã commit nên có thể dùng hệ số cũ. Đường đọc nay join `AmbientTransactionScope` như các participant invoice, refund và subscription-usage. Ngoài transaction quyết toán, hành vi không đổi.

`ce8f7c4` làm hoá đơn overtime vượt gói thu được bằng tiền mặt và PayOS. Hoá đơn `kind='supplementary'` do khách trả chứ không do gói trả, nên payment mang `is_subscription_payment = false`; luật duy nhất chặn việc này là trigger `trg_payment_match_rental`. Migration 0034 sửa đúng chỗ đó. Code coi supplementary như overtime ở cả hai đường thu tiền (allow-list kind, điều kiện completed + có settlement liên kết, `checkout_started_at`, `canCash`, `canCreateStaffCheckout`), ghi payment type là `'overtime'` để các truy vấn và tổng hợp doanh thu cũ vẫn đúng, và bỏ các chỗ từ chối cứng `INVOICE_NOT_PAYABLE`. `GetPrimaryInvoiceIdAsync` nay chỉ trả `kind='rental'`, đóng một đường hoàn tiền sai rental trước đó không chạm tới được chỉ vì rental gói không có tiền cọc.

`7eb105a` thu VAT trên hoá đơn supplementary bằng cách coi `invoices.amount` là tổng đã gồm VAT, đúng quy ước mà màn hình hoá đơn của khách và hoá đơn thuê trực tiếp đang dùng. Đường quyết toán gói trước đó ghi số trước VAT vào `amount`, nên thu thiếu 300.000 trên hoá đơn phải thu 330.000. Bản sửa cộng VAT ở đúng một caller truyền số trước VAT; `CreateExcessInvoiceAsync` vẫn ghi đúng tổng được truyền vào, vì caller overtime trực tiếp truyền `plan.Shortfall` vốn đã gồm VAT. Cộng VAT bên trong method dùng chung sẽ thu hai lần cho đường thuê trực tiếp. `Phase0Harness.PayableInvoiceTotalAsync` đổi sang cộng `amount` thay vì `amount + vat_amount`: đó là sửa dụng cụ đo, còn kỳ vọng 330.000 giữ nguyên.

`356b6c7` cho route lỗi thời `POST /api/rental/{id}/status/{status}` gọi `IRentalLifecycleService` thay vì chạy state machine thứ hai. Route, verb, policy `CustomerService`, header `Deprecation` và body 200 giữ nguyên; năm trạng thái được nhận ánh xạ sang `ConfirmAsync`, `StartAsync`, `ReturnAsync(id, null, ...)`, `CompleteAsync`, `CancelAsync`, trạng thái lạ trả 400 kèm danh sách hợp lệ. Route cũ trước đây không ghi `rental_status_history` và bỏ qua quyết toán, nên rental hoàn tất qua nó không được lập hoá đơn, hoàn tiền hay cộng điểm. Ba thay đổi hành vi được owner chấp nhận, mỗi cái có test chạy qua HTTP thật: `completed` chạy quyết toán đầy đủ, `cancelled` áp phí huỷ và chính sách hoàn tiền, `renting` đòi GPLX đã duyệt và còn hạn. Mã lỗi là mã của lifecycle service, không dịch lại về text `InvalidOperationException` cũ. `IOperationsService.ChangeRentalStatusAsync` cố ý không xoá vì còn khoảng mười test gọi trực tiếp; nó mang ghi chú XML rằng không còn truy cập được qua HTTP và đã được ghi thành nợ.

## Migration 0033 và 0034

Thứ tự chạy là 0033 rồi 0034. Runner tự phát hiện file `*.sql` mới trong `db/migrations` và áp dụng theo thứ tự tên, không cần đăng ký trong mã ([README](../db/migrations/README.md)). Cả hai migration đều idempotent, mọi câu lệnh đều có guard, không có thao tác phá dữ liệu, chạy lại được. Migration đã áp dụng thì không sửa.

`0033_rental_settlement_integrity.sql` mở `invoices.kind` cho `'supplementary'`, sửa `fn_block_invoice_for_subscription` để chỉ kind đó đi qua trên rental gói, thêm `ux_invoices_rental_supplementary` (tối đa một hoá đơn supplementary mỗi rental) và bảng `subscription_hour_ledger` với `UNIQUE(rental_id)`.

`0034_supplementary_invoice_collection.sql` sửa `fn_check_payment_matches_rental` theo ba bước có thứ tự: payment gắn invoice phải thuộc đúng rental của invoice đó, kiểm trước mọi return sớm — điều này đóng một lỗ có sẵn cho cả thuê trực tiếp; return sớm cho plan-purchase giữ nguyên nhưng nằm sau bước trên; và `kind='supplementary'` bỏ qua luật `is_subscription_payment` phải bằng `rentals.is_subscription`. Trigger `trg_payment_match_rental` được drop và tạo lại với `invoice_id` trong danh sách `UPDATE OF`, vì thiếu cột đó thì `UPDATE payments SET invoice_id = ...` không kích hoạt trigger. Hai CHECK mới `ck_payments_plan_purchase_shape` và `ck_payments_invoice_needs_rental` được thêm `NOT VALID` rồi validate.

## Không cần backfill VAT khi triển khai

Message của commit `7eb105a` kết luận rằng bản triển khai nào đang giữ hoá đơn overtime hoặc supplementary phải chạy `UPDATE invoices SET amount = amount + COALESCE(vat_amount,0) WHERE kind IN ('overtime','supplementary')` trước khi phát hành. Kết luận đó sai và chạy theo sẽ thu thừa tiền của khách.

Bằng chứng đọc lại từ `main` (`e438f7f`):

- `main:src/VehicleRental.Domain/Lifecycle/RentalLifecycleDomain.cs:175-177` tính `vat` rồi `total = amount + vat`, nên `OvertimeTotal` đã gồm VAT.
- `main:src/VehicleRental.Application/Lifecycle/RentalLifecycleService.cs:185-186` tạo hoá đơn overtime với `amount = settlementCalc.Shortfall`, mà `Shortfall = overtimeTotal - depositDeducted` (`SettlementCalculator`, cùng file domain, dòng 317-336). Vì vậy `invoices.amount` của hoá đơn overtime trên main đã gồm VAT.
- `main:src/VehicleRental.Infrastructure/Repositories/NpgsqlPaymentRepository.Cash.cs:27,66` thu tiền mặt đúng bằng `invoices.amount` và không cộng thêm `vat_amount`.
- `kind='supplementary'` không tồn tại trên main: `git grep -i supplementary main -- src` không trả kết quả. Kind này do migration 0033 của nhánh sinh ra.

Kết luận: production không cần backfill VAT. Chỉ một database dev/test đã chạy `ce8f7c4` trước `7eb105a` mới có thể đang giữ hoá đơn supplementary ghi số trước VAT, và chỉ những dòng `kind='supplementary'` ở đó mới cần sửa; hoá đơn overtime không bao giờ bị ảnh hưởng. Không rewrite lịch sử git: câu sai vẫn nằm trong message `7eb105a`, đính chính nằm ở đây và ở CHANGELOG.

## Kiểm chứng

Số dưới đây là writer tự báo trong message từng commit, không đo lại trong lượt viết tài liệu này.

- `9b2e5b4`: build 0 warning; Phase0 6/6; toàn suite 175 + 6 + 132 + 964 = 1.277 đạt, 0 fail.
- `e56372b`: build 0 warning; 1.280 đạt (+3 test mới); probe `pg_stat_activity` phía server thấy 1 backend trong lúc quyết toán thay vì 2; data source `Max Pool Size=1` tái hiện lỗi cạn pool cũ và nay đạt; đọc loyalty trong transaction thấy điểm vừa ghi.
- `ce8f7c4`: build 0 warning; 1.289 đạt (+9). Migration chạy lại hai lần trên DB nháp dựng từ `Init.sql` + `0002..0033`, schema dump giống nhau; cả hai CHECK validate trên DB rỗng và DB seed demo (0 dòng vi phạm).
- `7eb105a`: build 0 warning; Phase0 6/6; 1.289 đạt. Hoá đơn overtime trực tiếp `amount=340000 vat=40000` so với 340.000 phải thu; hoá đơn supplementary `amount=330000 vat=30000`; cash và PayOS đều thu tổng, webhook từ chối số trước VAT.
- `356b6c7`: build 0 warning; Phase0 6/6; 1.299 đạt (+10 ca, toàn bộ từ file test mới).
- Lượt sửa tài liệu và chú thích hiện tại chỉ chạy `git diff --check` và `dotnet build` (0 warning, 0 error), không chạy lại toàn suite vì không đổi code thực thi. Reviewer độc lập báo tại `248de35`: toàn suite Neon 1.422 đạt, 0 fail (Domain 175, Architecture 6, Application 133, Integration 1.108).

## Giới hạn và việc còn lại

- Phase 2 không có bản ghi review độc lập nào trong repo. Năm commit trên chưa được reviewer riêng kiểm tại thời điểm viết; mọi số đo ở mục trên là writer tự báo. Bản ghi Phase 3 ([2026-11-05-slice-extraction.md](2026-11-05-slice-extraction.md)) mới có phần review độc lập.
- Hai CHECK do `0034` thêm chỉ được validate trên database rỗng và database seed demo. Nếu production đang có dòng `payments` vi phạm — payment gắn invoice nhưng thiếu `rental_id`, hoặc `plan_id` trên bản ghi không phải mua gói — thì `VALIDATE CONSTRAINT` sẽ fail và migration 0034 dừng giữa đường. Cần chạy thử trên bản sao dữ liệu production trước khi phát hành.
- `0033` thay `ck_invoices_kind` và tạo bảng mới; chưa kiểm với dữ liệu production thật.
- Message `7eb105a` vẫn chứa hướng dẫn backfill VAT sai vì không rewrite lịch sử; đính chính chỉ nằm trong tài liệu này.
- `IOperationsService.ChangeRentalStatusAsync` vẫn còn và khoảng mười test gọi trực tiếp; nợ này đã ghi ở tài liệu Phase 3.
- Tài liệu D2/SRS và FE cho thanh toán supplementary vẫn chưa hoàn tất.
