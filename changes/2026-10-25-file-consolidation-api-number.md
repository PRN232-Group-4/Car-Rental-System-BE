# Gộp Customer detail và tên API — 2026-10-25

## Kết quả

- Gộp `CustomersController.Detail.cs` vào `CustomersController.cs` và `CustomerService.Detail.cs` vào `CustomerService.cs`; bỏ partial không còn cần. Source `src` giảm 236 → 234 file, không còn file `*Detail*`. Giữ DTO `CustomerDetail` và các test riêng.
- API collection giữ số nhiều (`/api/customers`, `/api/rentals`); resource có ID dùng số ít (`/api/customer/{id}`, `/api/rental/{id}`). Tài nguyên con áp dụng tương tự; action, singleton và `staff`/`media` giữ tên đúng nghĩa. POST tạo vào collection giữ số nhiều. Lịch sử phân công thực tế là danh sách nên giữ `assignments`.
- Đồng bộ controller attributes, Swagger response metadata, docs/API.md, FE adapters, schema/snapshot và mock e2e. Rà 179 action không có duplicate method/route shape hoặc plural collection ngay trước ID. UI navigation giữ nguyên.
- Location của Created dùng URL root-relative `/api/...`; tạo rental trả URL resource `/api/rental/{id}`.
- Upload hợp đồng lưu URL mới. Dữ liệu hợp đồng đã lưu URL `/api/rentals/{id}/contract/document` được chuyển thành URL canonical khi đọc ở cả EF projection và ADO mapper; giữ nhận diện hợp đồng đã upload. Không migration hoặc alias public cho URL cũ.
- Không đổi quyền, response envelope hoặc nghiệp vụ. Đây là đổi URL không tương thích ngược: BE/FE và client bên ngoài cần chuyển đồng bộ.

## Kiểm chứng

- `dotnet build Car-Rental-System-BE.sln --no-restore -m:1 -warnaserror --disable-build-servers`: đạt, 0 warning/error.
- Toàn bộ BE: **1.254/1.254 pass, 0 fail, 0 skipped**, PostgreSQL 18 tạm trên localhost, session UTC; report `artifacts/api-number-test-results/api-number-final.trx`.
- Test bổ sung kiểm tra Swagger collection/singleton, không có alias plural cũ và đọc hợp đồng có URL legacy. Nhóm HTTP/Location chạy lại sau chỉnh Location cuối: **143/143 pass, 0 fail/skip**; report `artifacts/api-number-test-results/api-number-location-final.trx`.
- FE typecheck đạt; 12 file test liên quan, **97/97 pass**. Đã đồng bộ 36 file mock e2e; chưa chạy Playwright/browser suite.
- Review độc lập routes, Location, URL persisted và FE calls; `git diff --check` sạch.
- Sandbox ban đầu chặn kết nối testhost; lượt hoàn tất chạy ngoài sandbox với database test độc lập. Không dùng database ứng dụng.
- Chưa deploy hoặc kiểm chứng container. Snapshot OpenAPI FE được đổi URL trong phạm vi; không tái sinh các endpoint đã thiếu từ trước.

## Tài liệu

- [NFR-001](../specs/NFR-001-clean-architecture.md), [NFR-008](../specs/NFR-008-openapi.md)
- [ADR-013](../docs/decisions/ADR-013-api-resource-number.md), [API](../docs/API.md)
- [Rà soát gộp file](../docs/file-consolidation-review.md)
