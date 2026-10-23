# Sửa bàn giao đơn tự lái

Nguyên nhân: giao diện chỉ mở phân công tài xế cho đơn có tài xế; đơn tự lái đã chọn xe và đạt kiểm tra nhưng chưa có bản ghi vehicle_assignments, trong khi start bắt buộc bản ghi này cho mọi đơn.

Start tự lái kiểm tra lại xe hiện tại, kiểm định, GPLX, thanh toán và khả dụng/bảo dưỡng trong transaction giữ khóa điều phối/xe/đơn; khi chưa có phân công thì ghi một assignment driver_staff_id=NULL với người bàn giao và khoảng thuê còn lại trước khi chuyển renting. Tái sử dụng phân công hiện hữu đúng xe, chặn phân công cũ sai xe/đơn tự lái chứa tài xế. Đơn yêu cầu tài xế vẫn phải được phân tài xế thực tế trước bàn giao. Không tạo dữ liệu kiểm định/thanh toán giả.

Regression: Application không đòi thao tác phân tài xế riêng cho tự lái; PostgreSQL kiểm chứng hourly/longterm, phân công mới/tái sử dụng, lặp/đồng thời một lần ghi; chặn thiếu kiểm định, GPLX chưa duyệt/hết hạn, unpaid, bảo dưỡng, bằng chứng/phân công xe cũ và đơn có tài xế nhưng chưa phân tài xế thực tế.

Kiểm chứng: `dotnet build tests/VehicleRental.Tests/VehicleRental.Tests.csproj --no-restore -m:1 -nr:false -warnaserror` đạt, không cảnh báo/lỗi. 63 tests thuộc HandoverIntegrationTests, RentalLifecycleServiceTests, RentalLifecycleIntegrationTests và RentalInspectionModesTests đạt trên PostgreSQL 18 cục bộ tách biệt; session Timezone=UTC để khớp quy ước timestamp UTC. `git diff --check` đạt. Chưa triển khai backend đang chạy.
