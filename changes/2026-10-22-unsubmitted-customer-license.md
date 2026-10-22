# Phân biệt hồ sơ GPLX chưa gửi và chờ duyệt

- Đăng ký thường và Google tạo `customers(user_id)` với DB mặc định `license_status=pending`, trước khi khách gửi GPLX. GET trước đây chuyển nguyên giá trị mặc định tới giao diện, làm tài khoản mới bị nhận là đang chờ duyệt.
- Application chuẩn hóa GET thành `status=none` khi chưa có số GPLX (null/rỗng/khoảng trắng), cùng quy tắc danh sách khách hàng hiện tại. Không sửa dữ liệu DB hoặc thay đổi trạng thái hồ sơ đã có số GPLX.
- Giữ bản ghi khác null cho customer chưa gửi: MediaService vẫn xác minh customer tồn tại và cho phép upload lần đầu, sau đó lưu `pending`.
- Bổ sung kiểm thử HTTP/envelope cho trạng thái mới/cũ, không mutate khi đọc, customer không tồn tại, upload lần đầu và Swagger. Kiểm chứng: `dotnet test tests/VehicleRental.Tests/VehicleRental.Tests.csproj --filter FullyQualifiedName~CustomerLicenseUploadTests --no-restore -c Release -v minimal`: **47/47 pass, 0 skipped**. Release tránh DLL Debug bị khóa bởi server local đang chạy; chưa deploy.
