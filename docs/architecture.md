# Kiến trúc

API là modular monolith theo Clean Architecture: Web API gọi Application; Application chỉ biết Domain và các port; Infrastructure hiện thực Npgsql, Redis, Brevo. Dependency đi vào trong, composition ở `Program.cs`.

`RegistrationWorkflow` là use case hiện có: controller → workflow → Redis pending registration/Brevo → `IUserRepository` → PostgreSQL. Redis cũng là `ITicketStore`; cookie chứa opaque session key, không chứa claims ticket.

Schema PostgreSQL không có FK theo chủ đích. Repository transaction hóa thao tác nhiều bảng; use case phải kiểm tra logical ID trước ghi. Khi có aggregate Rental, dùng transaction và lock/constraint thích hợp để chống double booking.
