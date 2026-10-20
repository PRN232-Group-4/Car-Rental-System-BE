# FR-039 — Hoàn thiện dữ liệu backend cho giao diện

Ngày: 2026-10-20. Nguồn yêu cầu: `APIfield còn thiếu để bỏ hoàn toàn.txt`.

- Customer rental summary bổ sung biển số, loại xe, quyền hủy và nextAction theo cùng quy tắc chi tiết; facility trả `{id,name,phone}` hoặc null ở cả list/detail.
- `subscriptionHoursUsed` là giờ lịch thuê đã ghi nhận cộng giờ vượt hạn đã quyết toán của đơn theo gói; đơn thường null. Không phải tổng tiêu thụ của khách trên mọi đơn; đơn hủy vẫn phản ánh giờ đã trừ vì luồng hiện tại không hoàn giờ.
- Catalog list/detail trả thuộc tính kỹ thuật thật và features[] được lưu bền vững. `supportsSubscription` theo khả năng thuê theo giờ (`hourlyPrice != null`) như search hiện tại. `driverAvailable` chỉ có giá trị khi có cửa sổ thời gian đủ để đánh giá; catalog không có cửa sổ trả null.
- Chính sách trả vehicleTypeName, managedBy (staff ID nullable), createdAt từ DB; tạo mới ghi managedBy của người thao tác.
- Assignment và lịch tài xế trả vehicleName/licensePlate/bufferMinutes từ xe và assignment thật.
- Không điền giả năm sản xuất, cấu hình xe, hotline hoặc lịch sử chưa được thu thập.

Acceptance: schema migration idempotent; kiểm tra DB cho projections, metadata và pagination; HTTP/Swagger tiếp tục envelope sáu trường. Các API quản trị, vận hành, dashboard có spec riêng trong cùng đợt.


Kiểm chứng 2026-10-20: build solution -warnaserror và bộ 325 test HTTP/Application/PostgreSQL liên quan đạt, gồm các acceptance ở trên. Database tạm độc lập; chưa deploy hoặc áp dụng migration vào database ứng dụng.
