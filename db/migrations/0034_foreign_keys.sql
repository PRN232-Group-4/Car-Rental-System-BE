-- Thêm foreign key cho mọi quan hệ logic đã ghi chú trong Init.sql và các migration (thay quyết định no-FK của ADR-001).
-- Mỗi FK được tạo NOT VALID (ràng buộc ngay mọi INSERT/UPDATE mới, không quét dữ liệu cũ) rồi thử VALIDATE.
-- Nếu dữ liệu cũ còn bản ghi mồ côi, VALIDATE bị bỏ qua kèm WARNING để không chặn khởi động; xử lý dữ liệu rồi
-- chạy lại `ALTER TABLE <bảng> VALIDATE CONSTRAINT <tên>` thủ công.
-- Cột đã có FK (staff_ratings, rental_status_history.rental_id, rental_settlements) được bỏ qua.
-- ON DELETE CASCADE chỉ dùng cho bảng con thuần (join table, ảnh, chi tiết xe, telemetry); còn lại RESTRICT
-- (NO ACTION) để Application tiếp tục chặn xóa có lịch sử nghiệp vụ.
DO $$
DECLARE
  fk RECORD;
  constraint_name TEXT;
BEGIN
  FOR fk IN SELECT * FROM (VALUES
    ('user_roles', 'user_id', 'users', 'id', 'CASCADE'),
    ('user_roles', 'role_id', 'roles', 'id', 'CASCADE'),
    ('staffs', 'user_id', 'users', 'id', 'NO ACTION'),
    ('staffs', 'department_id', 'departments', 'id', 'NO ACTION'),
    ('staffs', 'facility_id', 'facilities', 'id', 'NO ACTION'),
    ('customers', 'user_id', 'users', 'id', 'NO ACTION'),
    ('vehicles', 'facility_id', 'facilities', 'id', 'NO ACTION'),
    ('vehicles', 'vehicle_type_id', 'vehicle_types', 'id', 'NO ACTION'),
    ('vehicles', 'created_by', 'staffs', 'id', 'SET NULL'),
    ('vehicle_imgs', 'vehicle_id', 'vehicles', 'id', 'CASCADE'),
    ('car_detail', 'vehicle_id', 'vehicles', 'id', 'CASCADE'),
    ('motorbike_detail', 'vehicle_id', 'vehicles', 'id', 'CASCADE'),
    ('electric_car_detail', 'vehicle_id', 'vehicles', 'id', 'CASCADE'),
    ('electric_motorbike_detail', 'vehicle_id', 'vehicles', 'id', 'CASCADE'),
    ('rental_policies', 'vehicle_type_id', 'vehicle_types', 'id', 'NO ACTION'),
    ('rental_policies', 'managed_by', 'staffs', 'id', 'SET NULL'),
    ('customer_subscriptions', 'customer_id', 'customers', 'id', 'NO ACTION'),
    ('customer_subscriptions', 'plan_id', 'subscription_plans', 'id', 'NO ACTION'),
    ('rentals', 'customer_id', 'customers', 'id', 'NO ACTION'),
    ('rentals', 'vehicle_id', 'vehicles', 'id', 'NO ACTION'),
    ('rentals', 'policy_id', 'rental_policies', 'id', 'NO ACTION'),
    ('rentals', 'customer_subscription_id', 'customer_subscriptions', 'id', 'NO ACTION'),
    ('hour_rental_detail', 'rental_id', 'rentals', 'id', 'NO ACTION'),
    ('hour_rental_detail', 'staff_pickup_id', 'staffs', 'id', 'NO ACTION'),
    ('longterm_rental_contract', 'rental_id', 'rentals', 'id', 'NO ACTION'),
    ('longterm_rental_contract', 'staff_pick_up_id', 'staffs', 'id', 'NO ACTION'),
    ('vehicle_assignments', 'rental_id', 'rentals', 'id', 'NO ACTION'),
    ('vehicle_assignments', 'vehicle_id', 'vehicles', 'id', 'NO ACTION'),
    ('vehicle_assignments', 'driver_staff_id', 'staffs', 'id', 'NO ACTION'),
    ('vehicle_assignments', 'supersedes_id', 'vehicle_assignments', 'id', 'NO ACTION'),
    ('inspections', 'rental_id', 'rentals', 'id', 'NO ACTION'),
    ('inspections', 'vehicle_id', 'vehicles', 'id', 'NO ACTION'),
    ('inspections', 'staff_id', 'staffs', 'id', 'NO ACTION'),
    ('invoices', 'rental_id', 'rentals', 'id', 'NO ACTION'),
    ('payments', 'rental_id', 'rentals', 'id', 'NO ACTION'),
    ('payments', 'invoice_id', 'invoices', 'id', 'NO ACTION'),
    ('payments', 'plan_id', 'subscription_plans', 'id', 'NO ACTION'),
    ('payments', 'customer_id', 'customers', 'id', 'NO ACTION'),
    ('vehicle_maintenance', 'vehicle_id', 'vehicles', 'id', 'NO ACTION'),
    ('vehicle_maintenance', 'facility_id', 'facilities', 'id', 'NO ACTION'),
    ('vehicle_maintenance', 'assigned_staff_id', 'staffs', 'id', 'NO ACTION'),
    ('vehicle_maintenance', 'rental_id', 'rentals', 'id', 'NO ACTION'),
    ('vehicle_maintenance', 'inspection_id', 'inspections', 'id', 'SET NULL'),
    ('reports', 'customer_id', 'customers', 'id', 'NO ACTION'),
    ('reports', 'rental_id', 'rentals', 'id', 'NO ACTION'),
    ('reports', 'maintenance_id', 'vehicle_maintenance', 'id', 'NO ACTION'),
    ('reports', 'reporter_staff_id', 'staffs', 'id', 'NO ACTION'),
    ('reports', 'vehicle_id', 'vehicles', 'id', 'NO ACTION'),
    ('driver_shifts', 'driver_staff_id', 'staffs', 'id', 'NO ACTION'),
    ('vehicle_replacements', 'rental_id', 'rentals', 'id', 'NO ACTION'),
    ('vehicle_replacements', 'report_id', 'reports', 'id', 'NO ACTION'),
    ('vehicle_replacements', 'old_vehicle_id', 'vehicles', 'id', 'NO ACTION'),
    ('vehicle_replacements', 'new_vehicle_id', 'vehicles', 'id', 'NO ACTION'),
    ('vehicle_replacements', 'old_driver_staff_id', 'staffs', 'id', 'NO ACTION'),
    ('vehicle_replacements', 'new_driver_staff_id', 'staffs', 'id', 'NO ACTION'),
    ('vehicle_bookings', 'rental_id', 'rentals', 'id', 'NO ACTION'),
    ('vehicle_bookings', 'vehicle_id', 'vehicles', 'id', 'NO ACTION'),
    ('tracking_devices', 'vehicle_id', 'vehicles', 'id', 'CASCADE'),
    ('vehicle_positions', 'vehicle_id', 'vehicles', 'id', 'CASCADE'),
    ('vehicle_positions', 'device_id', 'tracking_devices', 'id', 'CASCADE'),
    ('vehicle_last_positions', 'vehicle_id', 'vehicles', 'id', 'CASCADE'),
    ('vehicle_last_positions', 'device_id', 'tracking_devices', 'id', 'CASCADE'),
    ('geofences', 'facility_id', 'facilities', 'id', 'NO ACTION'),
    ('geofence_events', 'vehicle_id', 'vehicles', 'id', 'CASCADE'),
    ('geofence_events', 'rental_id', 'rentals', 'id', 'NO ACTION'),
    ('geofence_events', 'geofence_id', 'geofences', 'id', 'CASCADE'),
    ('tracking_alerts', 'vehicle_id', 'vehicles', 'id', 'CASCADE'),
    ('tracking_alerts', 'rental_id', 'rentals', 'id', 'NO ACTION'),
    ('rental_status_history', 'actor_user_id', 'users', 'id', 'NO ACTION'),
    ('customer_loyalty_transactions', 'customer_id', 'customers', 'id', 'NO ACTION'),
    ('customer_loyalty_transactions', 'rental_id', 'rentals', 'id', 'NO ACTION'),
    ('rental_orders', 'rental_id', 'rentals', 'id', 'NO ACTION'),
    ('rental_orders', 'payment_id', 'payments', 'id', 'NO ACTION'),
    ('user_administration_audit', 'user_id', 'users', 'id', 'NO ACTION'),
    ('user_administration_audit', 'actor_user_id', 'users', 'id', 'NO ACTION'),
    ('report_history', 'report_id', 'reports', 'id', 'NO ACTION'),
    ('rating_moderation_history', 'rating_id', 'staff_ratings', 'id', 'NO ACTION'),
    ('inspection_assignments', 'rental_id', 'rentals', 'id', 'NO ACTION'),
    ('inspection_assignments', 'vehicle_id', 'vehicles', 'id', 'NO ACTION'),
    ('inspection_assignments', 'technician_staff_id', 'staffs', 'id', 'NO ACTION'),
    ('customer_licenses', 'customer_id', 'customers', 'id', 'NO ACTION'),
    ('rental_contract_documents', 'rental_id', 'rentals', 'id', 'NO ACTION')
  ) AS t(tbl, col, ref_tbl, ref_col, on_delete)
  LOOP
    -- Bỏ qua cột đã có FK từ migration trước.
    CONTINUE WHEN EXISTS (
      SELECT 1 FROM pg_constraint c
      JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = ANY (c.conkey)
      WHERE c.contype = 'f' AND c.conrelid = fk.tbl::regclass AND a.attname = fk.col);

    constraint_name := left('fk_' || fk.tbl || '_' || fk.col, 63);
    EXECUTE format('ALTER TABLE %I ADD CONSTRAINT %I FOREIGN KEY (%I) REFERENCES %I(%I) ON DELETE %s NOT VALID',
      fk.tbl, constraint_name, fk.col, fk.ref_tbl, fk.ref_col, fk.on_delete);
    BEGIN
      EXECUTE format('ALTER TABLE %I VALIDATE CONSTRAINT %I', fk.tbl, constraint_name);
    EXCEPTION WHEN foreign_key_violation THEN
      RAISE WARNING 'FK % chưa VALIDATE vì dữ liệu cũ có bản ghi mồ côi; vẫn ràng buộc dữ liệu ghi mới.', constraint_name;
    END;
  END LOOP;
END $$;

-- Index cho cột FK hay bị xóa/khóa từ bảng cha mà chưa có index.
CREATE INDEX IF NOT EXISTS ix_vehicle_imgs_vehicle_id ON vehicle_imgs(vehicle_id);
CREATE INDEX IF NOT EXISTS ix_user_roles_role_id ON user_roles(role_id);
CREATE INDEX IF NOT EXISTS ix_vehicle_positions_device_id ON vehicle_positions(device_id);
