# Bắt buộc đầy đủ thông tin GPLX khách hàng — 2026-10-21

Yêu cầu đủ số GPLX, hạng, ngày cấp, ngày hết hạn và ảnh khi gửi hồ sơ. HTTP bắt buộc `issueDate` cho multipart và JSON; JSON bắt buộc `imageUrl` và `expiryDate`. Application kiểm tra metadata trước upload, URL ảnh trước lưu. Hồ sơ đã lưu vẫn giữ nguyên kiểu dữ liệu đọc để tương thích dữ liệu cũ.

Frontend áp dụng cho `/driver-license` và `/profile`: đánh dấu trường bắt buộc, hiển thị tất cả lỗi inline, focus lỗi đầu tiên và giữ dữ liệu khi API/mạng lỗi. Mỗi lần gửi cần chọn file ảnh; `/profile` dùng trực tiếp API multipart GPLX thay cho upload ảnh xe rồi gửi URL. Giới hạn ảnh 5 MiB theo API; chặn tệp rỗng, sai định dạng, ngày cấp sau ngày hết hạn và GPLX hết hạn.

Kiểm chứng: backend 38/38 CustomerLicenseUploadTests đạt, bao gồm HTTP thật, envelope 6 trường, Swagger required, thiếu từng field, Application không cho thiếu ngày cấp/ảnh, upload thất bại không ghi dữ liệu. Frontend 24/24 test GPLX/inline-validation đạt với API giả lập, bao gồm hai form, gửi đầy đủ, tệp rỗng/quá lớn, ngày không hợp lệ và giữ dữ liệu khi lỗi. TypeScript và ESLint các file thay đổi đạt, không cảnh báo. Chưa deploy hoặc kiểm tra Cloudinary thật.
