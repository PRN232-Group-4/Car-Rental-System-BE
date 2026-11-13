# 2026-11-13 — Tích hợp Features layout và giữ ORM/FK

- **FR/NFR:** NFR-001, ADR-014, ADR-015
- **Loại:** refactor/merge

## Đã làm

- Merge `refactor/features-layout` vào `refactor/db` theo xác nhận của người dùng; chuyển module sang `Features/<Module>` trong bốn layer và test sang bốn project theo layer.
- Chuyển các triển khai ORM vào repository đã tách; tách row mapping theo module, giữ default và FK. Helper timestamp chuyển vào Persistence để dùng chung.
- Giữ quyết toán một transaction, ledger giờ gói, hóa đơn supplementary và guard hoàn tiền theo rental; các writer đi kèm nhánh Features được chuyển sang EF Core trên cùng transaction.
- Giữ nguyên checksum/nội dung SQL migration đã có; thêm mapping/FK cho ledger giờ gói.
- Chuyển regression kiến trúc/raw-query và FK vào các project test mới; cập nhật tài liệu cấu trúc.

## Kiểm chứng

- `dotnet build Car-Rental-System-BE.sln --no-restore -m:1 -warnaserror`: **0 warning/error**.
- Toàn suite trên PostgreSQL 18 tạm UTC: **1.466 passed, 0 failed, 0 skipped** (Domain 175, Application 133, Integration 1.145, Architecture 13).
- Catalog: **47 bảng nghiệp vụ, 90 FK**, tất cả validated và khớp EF model; hai FK mới thuộc ledger giờ gói.
- Không còn conflict marker; raw API chỉ còn trong `DbLocks` và DDL/file migration. Nội dung các migration cũ giữ nguyên.
- Sửa các điểm tương thích sau merge: quyền thu cash/checkout của supplementary invoice; seed actor của test HTTP phải có bản ghi users theo FK; bỏ ba ngoại lệ privacy đã hết hiệu lực khi helper timestamp chuyển vào Persistence.
- Không thay database ứng dụng, không deploy. Các migration settlement/supplementary đi kèm nhánh Features cần được áp dụng khi triển khai BE.
