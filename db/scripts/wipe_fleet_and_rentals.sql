-- XÓA TOÀN BỘ xe, đơn thuê, đơn bảo trì, phiếu kiểm tra và mọi dữ liệu gắn với chúng.
-- KHÔNG phải migration: runner không tự chạy file này. Chạy tay một lần, sau khi đã backup:
--   psql "$DATABASE_URL" -v ON_ERROR_STOP=1 -f db/scripts/wipe_fleet_and_rentals.sql
-- Giữ lại: tài khoản, khách hàng, nhân viên, chi nhánh, loại xe, chính sách thuê, gói đăng ký và các
-- thanh toán gói đăng ký không gắn đơn thuê. Điểm thành viên được tính lại từ giao dịch còn lại.
-- Toàn bộ chạy trong một transaction: lỗi ở bất kỳ bước nào sẽ hoàn tác hết.

BEGIN;

-- Hai bảng lịch sử được trigger khóa chỉ-ghi-thêm; tạm tắt trong transaction này rồi bật lại.
ALTER TABLE rental_status_history DISABLE TRIGGER trg_rsh_immutable;
ALTER TABLE customer_loyalty_transactions DISABLE TRIGGER trg_clt_immutable;

CREATE TEMP TABLE wipe_invoices ON COMMIT DROP AS SELECT id FROM invoices WHERE rental_id IS NOT NULL;
CREATE TEMP TABLE wipe_payments ON COMMIT DROP AS
  SELECT id FROM payments WHERE rental_id IS NOT NULL OR invoice_id IN (SELECT id FROM wipe_invoices);

DELETE FROM rental_orders;
DELETE FROM rental_settlements;
DELETE FROM customer_loyalty_transactions WHERE rental_id IS NOT NULL;
DELETE FROM subscription_hour_ledger WHERE rental_id IS NOT NULL;
DELETE FROM rating_moderation_history WHERE rating_id IN (SELECT id FROM staff_ratings WHERE rental_id IS NOT NULL);
DELETE FROM staff_ratings WHERE rental_id IS NOT NULL;
DELETE FROM rental_contract_documents;
DELETE FROM rental_status_history;
DELETE FROM payments WHERE id IN (SELECT id FROM wipe_payments);
DELETE FROM invoices WHERE id IN (SELECT id FROM wipe_invoices);

DELETE FROM vehicle_replacements;
DELETE FROM report_history WHERE report_id IN (
  SELECT id FROM reports WHERE rental_id IS NOT NULL OR vehicle_id IS NOT NULL OR maintenance_id IS NOT NULL);
DELETE FROM reports WHERE rental_id IS NOT NULL OR vehicle_id IS NOT NULL OR maintenance_id IS NOT NULL;
DELETE FROM vehicle_maintenance;
DELETE FROM inspection_assignments;
DELETE FROM inspections;
DELETE FROM vehicle_bookings;
DELETE FROM vehicle_assignments;
DELETE FROM geofence_events;
DELETE FROM tracking_alerts;
DELETE FROM hour_rental_detail;
DELETE FROM longterm_rental_contract;
DELETE FROM rentals;

-- Ảnh, chi tiết xe và telemetry tự xóa theo (ON DELETE CASCADE).
DELETE FROM vehicles;

UPDATE customers c SET loyalty_point = COALESCE(
  (SELECT SUM(t.points) FROM customer_loyalty_transactions t WHERE t.customer_id = c.id), 0);

ALTER TABLE rental_status_history ENABLE TRIGGER trg_rsh_immutable;
ALTER TABLE customer_loyalty_transactions ENABLE TRIGGER trg_clt_immutable;

COMMIT;
