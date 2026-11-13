# ADR-001 — Clean Architecture và repository theo aggregate
- Status: Accepted
- Ngày: 2026-09-30
## Bối cảnh
Cần tách nghiệp vụ thuê xe khỏi PostgreSQL/Redis/Brevo; schema ban đầu không có FK (đã thêm FK theo ADR-014).
## Quyết định
Bốn layer Domain, Application, Infrastructure, Web API. Repository theo aggregate root, không generic repository. Application kiểm tra toàn vẹn logic, Infrastructure bao transaction.
## Hệ quả
Code có thêm port/implementation nhưng test được use case độc lập; không được nhét SQL vào controller.

## Bổ sung — 2026-10-07
Tách `Application/Services`, `Application/Repositories` và `Infrastructure/Repositories`.
Controller gọi service interface; service điều phối repository/cache/email/payment ports.
OTP/password reset, session, staff credential invalidation, avatar và checkout/webhook orchestration
thuộc Application. Infrastructure giữ SQL, mapping, transaction và lock. JWT/OAuth/cookie là adapter HTTP.
Không để service kế thừa repository interface; service contract diễn tả use case, không công khai
thao tác hoàn tất payment bỏ qua xác minh webhook.

Tiến độ chuyển đổi: repository operations vẫn cần tách theo aggregate; các invariant phụ thuộc trạng thái
được đọc dưới lock vẫn giữ trong transaction hiện tại cho tới khi có Domain aggregate/transaction port phù hợp.

## Layout tham chiếu — 2026-10-07
Theo cấu trúc người dùng cung cấp, service/provider/repository contracts ở `Application/Interfaces`,
DTO ở `Application/DTOs`, validator ở `Application/Validators`, adapter ở `Infrastructure/Services`.
`IUserRepository` thuộc `Domain/Interfaces` vì chỉ dùng User entity/kiểu cơ bản; các repository port
trả DTO vẫn ở Application để Domain không phụ thuộc Application. Project HTTP giữ tên `VehicleRental.WebApi`;
HTTP middleware/auth/filter ở `Middlewares`. ValueObjects chỉ có README tới khi có model thực tế.
