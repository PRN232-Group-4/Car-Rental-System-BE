# Backlog tối ưu

| Ưu tiên | Việc | Lý do |
|---|---|---|
| P0 | Redis distributed rate limit email/IP | Chống abuse OTP khi scale nhiều instance. |
| P0 | Transactional outbox cho Brevo | Không mất/đúp email khi DB hoặc API lỗi. |
| P0 | Exclusion constraint/advisory lock lịch xe | Chống double-booking cạnh tranh. |
| P1 | Index availability/filter xe | Tối ưu FR-010 sau EXPLAIN thực tế. |
| P1 | Background job subscription/maintenance reminder | Tự động expiry/nhắc hạn. |
| P2 | Cache danh mục xe/policy | Giảm đọc DB; phải invalidation rõ ràng. |
