# 2026-10-15 — Sửa startup availability và rate limiter

- FR/NFR: FR-010.
- Nguyên nhân đầu tiên: composition root đăng ký dịch vụ availability/search nhưng bỏ sót `AvailabilityOptions`, làm DI validation ném AggregateException lúc `builder.Build()`.
- Sau khi khôi phục options, smoke test phát hiện thêm `UseRateLimiter()` thiếu `AddRateLimiter()`.

## Thay đổi

- Bind section `Availability`, `ValidateDataAnnotations`, `ValidateOnStart`; expose cùng instance `IOptions<AvailabilityOptions>.Value` cho các constructor nhận concrete options.
- Đăng ký policy `public-search` fixed window 60 request/phút/IP, không queue, dùng địa chỉ client sau forwarded headers; trả 429 và envelope JSON chung khi vượt giới hạn.
- Không cần thêm section appsettings khi dùng giá trị mặc định của AvailabilityOptions. Không đổi thuật toán, khoảng đệm hay điều kiện tìm xe; không đổi key, connection string hoặc database.

## Kiểm chứng

- `dotnet build Car-Rental-System-BE.sln --no-restore -warnaserror`: đạt, 0 warning/error.
- Test `VehicleSearchServiceTests`, `DomainAvailabilityTests`, `AssignmentServiceTests`: 44/44 đạt.
- Thêm `VehicleSearchApiContractTests` và `ApiResponseTests`: tổng 88/93 đạt. Năm lỗi ở `ApiSuccessResponseFilter_maps_all_vehicle_search_actions` do thiếu mapping `VehicleSearch.*` vốn đã có trong HEAD; không sửa trong thay đổi startup.
- `dotnet run --no-build --no-launch-profile` với Development, migration tắt, DB/Redis trỏ cổng localhost giả: không còn lỗi DI hoặc thiếu rate limiter, tiến tới hosted services rồi dừng ở Redis không kết nối được như dự kiến.
- Không chạy migration hay kết nối dịch vụ thật; chưa xác nhận ứng dụng lên HTTP đầy đủ hoặc kiểm thử HTTP 429.
