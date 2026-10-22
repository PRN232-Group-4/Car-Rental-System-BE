-- Confirmation provenance distinguishes a physical inspection from an explicit approval.
ALTER TABLE inspections ADD COLUMN confirmation_source VARCHAR(30) NOT NULL DEFAULT 'technician';
ALTER TABLE inspections ADD COLUMN confirmed_by VARCHAR(255);
ALTER TABLE inspections ADD CONSTRAINT ck_inspection_confirmation_source
    CHECK (confirmation_source IN ('technician','driver_default','admin_confirmation'));
ALTER TABLE longterm_rental_contract ADD COLUMN has_driver BOOLEAN NOT NULL DEFAULT false;

CREATE FUNCTION confirm_driver_pre_inspection(rental_key INTEGER) RETURNS void LANGUAGE plpgsql AS $$
BEGIN
    INSERT INTO inspections(rental_id,vehicle_id,inspection_type,is_ok,damage_notes,inspected_at,confirmation_source,confirmed_by)
    SELECT r.id,r.vehicle_id,'pre_rental',true,'Mặc định đạt kiểm tra trước thuê cho đơn có tài xế.',
        now() AT TIME ZONE 'utc','driver_default','system'
    FROM rentals r
    LEFT JOIN hour_rental_detail h ON h.rental_id=r.id
    LEFT JOIN longterm_rental_contract l ON l.rental_id=r.id
    WHERE r.id=rental_key AND r.status IN ('pending','pre_inspection') AND r.started_at IS NULL
        AND COALESCE(h.has_driver,l.has_driver,false)
    ON CONFLICT(rental_id,vehicle_id,inspection_type) DO NOTHING;
END $$;

CREATE FUNCTION confirm_driver_detail_trigger() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    PERFORM confirm_driver_pre_inspection(NEW.rental_id);
    RETURN NEW;
END $$;
CREATE TRIGGER trg_hour_driver_pre_inspection AFTER INSERT OR UPDATE OF has_driver ON hour_rental_detail
    FOR EACH ROW EXECUTE FUNCTION confirm_driver_detail_trigger();
CREATE TRIGGER trg_longterm_driver_pre_inspection AFTER INSERT OR UPDATE OF has_driver ON longterm_rental_contract
    FOR EACH ROW EXECUTE FUNCTION confirm_driver_detail_trigger();
CREATE FUNCTION confirm_driver_vehicle_trigger() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    PERFORM confirm_driver_pre_inspection(NEW.id);
    RETURN NEW;
END $$;
CREATE TRIGGER trg_replacement_driver_pre_inspection AFTER UPDATE OF vehicle_id ON rentals
    FOR EACH ROW WHEN (OLD.vehicle_id IS DISTINCT FROM NEW.vehicle_id)
    EXECUTE FUNCTION confirm_driver_vehicle_trigger();

-- Upgrade outstanding driver bookings without overwriting physical pass/fail evidence.
SELECT confirm_driver_pre_inspection(r.id) FROM rentals r WHERE r.status IN ('pending','pre_inspection');
