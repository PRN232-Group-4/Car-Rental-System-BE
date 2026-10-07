# Tài liệu API BE — 2026-10-07

Liên quan: [NFR-008 — OpenAPI](../specs/NFR-008-openapi.md).

## Đã làm

- Tạo [docs/API.md](../docs/API.md) bằng tiếng Việt: đủ 90 controller endpoints, root/health và callback Google middleware.
- Mô tả method/route, quyền và phạm vi dữ liệu, body/query, schema response, envelope và mã HTTP thực tế sau filter.
- Thêm ví dụ đăng ký/login, upload, đặt xe, thanh toán, báo sự cố và gọi API từ frontend.
- Ghi đúng callback OAuth hiện tại và các khác biệt so với tài liệu cũ; nêu tính năng chưa có endpoint/payout worker.
- Liên kết từ docs index và cập nhật progress/acceptance tài liệu của NFR-008.

## Kiểm chứng

- Đối chiếu 90 route HTTP trong 11 controller với bảng endpoint: không thiếu route.
- Kiểm tra JSON minh họa và các link file/tài liệu nội bộ.
- Rà cookie/policy/DTO/response filter và các điều kiện nghiệp vụ liên quan trong repository.
- Không chạy build/test ứng dụng: thay đổi chỉ gồm Markdown, không đổi runtime/schema.

## Chưa làm và giả định

- Chưa gọi HTTP smoke test hoặc kiểm tra dịch vụ ngoài; không tuyên bố môi trường triển khai hoạt động.
- ID, email, giá, ngày và URL trong ví dụ là dữ liệu minh họa, cần thay theo môi trường sử dụng.
- Giữ NFR-008 In progress; versioning và metadata auth/examples trong Swagger chưa được triển khai ở thay đổi này.
