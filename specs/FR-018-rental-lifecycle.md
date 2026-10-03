# FR-018 — Hủy/hoàn tất rental
- **Status:** In progress; **Priority:** P0; **Milestone:** M4
State machine pending→pre→renting→post→completed/cancelled. Acceptance: illegal transition rejected, audit.

## Phân quyền cập nhật 2026-10-03

Phân quyền đổi trạng thái: Admin/Manager/CustomerService. Trước pre_inspection/renting cần pre-inspection đạt; trước post_inspection/completed cần post-inspection. GET rental kiểm tra owner Customer hoặc Driver có assignment hiện hành; Admin/Manager/CustomerService/Maintenance được đọc toàn bộ. Ngoài phạm vi trả 404.
