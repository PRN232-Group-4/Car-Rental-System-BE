# 2026-10-10 — Rule pagination cho API danh sách

- **FR/NFR:** NFR-008
- **Loại:** docs

## Đã làm

- Ghi rule bắt buộc vào AGENTS.md và Definition of Done: page=1, pageSize=20, tối đa 100.
- Chuẩn hóa dữ liệu page: items/page/pageSize/totalCount trong data của envelope chung.
- Bắt buộc validation ở HTTP/Application, 400 khi sai; thứ tự ổn định và trang rỗng giữ metadata.
- Cập nhật NFR-008, API reference và ADR-003; ghi rõ chuyển đổi API list cũ còn pending.

## Kiểm chứng

Kiểm tra diff whitespace và nội dung/link tài liệu. Chỉ thay đổi hướng dẫn/spec, không sửa runtime hoặc chạy lại build/test.
