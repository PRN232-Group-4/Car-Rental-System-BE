# Quy tắc nghiệp vụ

- System có một đơn vị vận hành, gồm Customer, Staff/Driver, Admin/Dispatcher, Maintenance staff.
- OTP tạo user/customer chỉ sau verify; TTL 10 phút, 5 lần sai, gửi lại sau 60 giây.
- Mật khẩu đăng ký/đăng nhập và mật khẩu mới khi tạo nhân viên/đổi/reset phải từ 6 đến 20 ký tự; không trim/cắt ngắn để xác thực. Ngoài khoảng trả 400 validation.
- Xe có một trạng thái vận hành; không có hai rental overlap, và maintenance/long-term block availability.
- Pre-inspection `is_ok=true` mới chuyển `renting`; post-inspection trước `completed`; fail khóa xe, staff report riêng, Manager giao bảo trì.
- Direct rental có invoice/payment; subscription rental không có invoice và trừ hạn mức gói.
