# Hướng dẫn phát triển Car Rental System

## Mục tiêu và phạm vi
Đây là **system** cho một đơn vị vận hành sở hữu đội xe; không phải marketplace đa chủ xe. Không thêm owner/partner/commission nếu chưa có ADR mới. Đọc `docs/`, `specs/`, `changes/` trước khi sửa mã.

## Kiến trúc bắt buộc
Giữ dependency một chiều: `Domain <- Application <- Infrastructure <- WebApi`.

- `Domain`: aggregate, value object, invariant; không EF/Npgsql/HTTP/Redis.
- `Application`: use case, command/query, port (repository/email/cache); không phụ thuộc Infrastructure/Web.
- `Infrastructure`: Npgsql, Redis, Brevo, repository implementation.
- `WebApi` (project root hiện tại): controller, DTO, middleware, composition root duy nhất.

Repository chỉ đại diện aggregate root. Không generic repository; controller không gọi Npgsql/Redis trực tiếp. Schema cố ý không có FK: Application phải xác minh logical reference và transaction khi đổi nhiều bảng.

## Quy tắc nghiệp vụ bắt buộc
- Xác thực: email lower-case; OTP 6 số có TTL, cooldown, giới hạn sai và rate limit email/IP. Không log OTP, API key, mật khẩu, cookie hoặc connection string. Tạo `users`/`customers` chỉ sau verify thành công.
- Mật khẩu: PBKDF2/Argon2 có salt; đổi/reset mật khẩu thu hồi mọi session.
- Session: cookie `HttpOnly`, `Secure`, `SameSite=Lax`; ticket Redis với absolute + sliding expiry; logout xóa ticket.
- Thuê xe: không chồng lịch; long-term khóa xe; pre-inspection đạt trước `renting`; post-inspection trước `completed`; fail khóa xe, staff report riêng, Manager giao maintenance (FR-031).
- Payment: subscription rental không invoice; direct rental phải qua invoice; snapshot giá/policy lúc đặt.

## Quy trình mỗi thay đổi
1. Đọc/cập nhật spec FR/NFR trước.
2. Làm use case nhỏ nhất, thêm test Domain/Application hoặc integration khi dùng DB/Redis.
3. Cập nhật acceptance criteria và `docs/progress.md`.
4. Tạo `changes/YYYY-MM-DD-<slug>.md`, cập nhật `changes/CHANGELOG.md`.
5. Giả định vào `docs/open-questions.md`; quyết định bền vững vào ADR; tối ưu để sau vào backlog.

## Definition of Done
- Validate ở HTTP boundary, invariant ở Application/Domain, lỗi trả ProblemDetails.
- Mỗi API mới phải xuất hiện trong Swagger ở non-production: controller dùng `ApiController`, route rõ ràng, request/response status codes và auth requirement được mô tả. Không ẩn endpoint khỏi Swagger trừ khi có lý do bảo mật được ghi trong spec.
- Mỗi API mới phải trả success message đúng ngữ cảnh nghiệp vụ, không dùng message chung chung. Bổ sung mapping `Controller.Action` tương ứng trong `src/VehicleRental.WebApi/Responses/ApiSuccessResponseFilter.cs` và kiểm tra message phản ánh đúng hành động (ví dụ: tạo, cập nhật, phê duyệt, hủy hoặc truy xuất).
- Không secret trong Git; Options `ValidateOnStart`, `.env.example` đầy đủ.
- Build/test/Docker chạy; container non-root.
- Specs, changes và progress phản ánh đúng đã làm/chưa làm.

## Khi chưa rõ
Không dừng lại: chọn phương án an toàn, đơn giản, dễ đổi và ghi giả định/lý do. Chỉ hỏi khi quyết định đổi phạm vi nghiệp vụ hoặc chi phí/hạ tầng.
