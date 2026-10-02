# FR-011 — Rental policy
- **Status:** In progress; **Priority:** P1; **Milestone:** M3

Admin và Manager cấu hình VAT/overtime/deposit bằng cách tạo policy version mới. Policy không được sửa trực tiếp; chỉ policy active được áp dụng và rental giữ snapshot giá tại thời điểm đặt.

| Method | Endpoint | Roles | Mô tả |
|---|---|---|---|
| GET | `/api/rental-policies` | Admin, Manager | Danh sách policy, gồm cả policy đã ngừng áp dụng. |
| POST | `/api/rental-policies` | Admin, Manager | Tạo một policy version mới. |

Không có `PUT`/`PATCH`/`DELETE` cho rental policy. Khi cần đổi điều khoản, tạo policy mới; policy cũ vẫn được giữ làm lịch sử cho các rental đã tạo.
