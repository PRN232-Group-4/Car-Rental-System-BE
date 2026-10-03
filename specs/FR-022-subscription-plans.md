# FR-022 — Gói subscription
- **Status:** In progress
- **Priority:** P1
- **Milestone:** M6

## Mô tả
CRUD `subscription_plans`. Validate ở HTTP boundary: `price >= 0.01`, `duration_days` 1..3650, `included_hours >= 0`, `name` bắt buộc. Plan inactive không mua mới. Xóa plan đã có `customer_subscriptions` là soft-delete (`is_active=false`) để giữ logical reference (schema không FK); plan chưa dùng thì xóa cứng.

## API
| Method | Path | Auth | Mô tả |
|---|---|---|---|
| GET | `/api/subscription-plans?all=` | Public | Danh sách active; chỉ Admin/Manager được dùng all=true để xem plan inactive |
| GET | `/api/subscription-plans/{id}` | Public | Chi tiết plan |
| POST | `/api/subscription-plans` | Admin, Manager | Tạo plan |
| PUT | `/api/subscription-plans/{id}` | Admin, Manager | Cập nhật plan |
| DELETE | `/api/subscription-plans/{id}` | Admin | Xóa/ngừng kích hoạt plan |
| POST | `/api/subscription-plans/{id}/purchase` | Customer | Mua plan active |

## Acceptance criteria
- [x] Validate duration/hours/price
- [x] Plan inactive không mua mới (`BuyPlanAsync` lọc `is_active=true`)
- [x] DELETE giữ logical reference bằng soft-delete khi plan đã được dùng
- [x] Integration test khi chạm hạ tầng

## Thay đổi
- `changes/2026-10-01-core-rental-apis.md`
- `changes/2026-10-01-invoice-payment-subscription.md`
