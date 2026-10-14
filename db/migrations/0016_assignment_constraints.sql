-- 0012_assignment_constraints.sql
-- FR-014: Enforce valid status vocabulary, driver no-overlap exclusion constraint with buffer, and assignment tracking columns.

CREATE EXTENSION IF NOT EXISTS btree_gist;

-- 1. Validate existing status vocabulary
DO $$
DECLARE bad INTEGER;
BEGIN
  SELECT count(*) INTO bad FROM vehicle_assignments
   WHERE status IS NULL OR status NOT IN ('assigned','reassigned','completed','cancelled');
  IF bad > 0 THEN RAISE EXCEPTION 'vehicle_assignments.status có % dòng ngoài tập hợp lệ', bad; END IF;
END $$;

ALTER TABLE vehicle_assignments ALTER COLUMN status SET NOT NULL;

DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_constraint WHERE conname = 'ck_va_status' AND conrelid = 'vehicle_assignments'::regclass
  ) THEN
    ALTER TABLE vehicle_assignments ADD CONSTRAINT ck_va_status
      CHECK (status IN ('assigned','reassigned','completed','cancelled'));
  END IF;
END $$;

-- 2. Add tracking and scheduling columns
ALTER TABLE vehicle_assignments
  ADD COLUMN IF NOT EXISTS reason          VARCHAR(30),
  ADD COLUMN IF NOT EXISTS note            VARCHAR(500),
  ADD COLUMN IF NOT EXISTS supersedes_id   INTEGER,
  ADD COLUMN IF NOT EXISTS ended_at        TIMESTAMP,
  ADD COLUMN IF NOT EXISTS starts_at       TIMESTAMP,
  ADD COLUMN IF NOT EXISTS ends_at         TIMESTAMP,
  ADD COLUMN IF NOT EXISTS buffer_minutes  INTEGER NOT NULL DEFAULT 60;

-- 3. Backfill starts_at/ends_at from rental details
UPDATE vehicle_assignments va SET
  starts_at = COALESCE((SELECT start_time FROM hour_rental_detail WHERE rental_id = va.rental_id),
                       (SELECT start_time FROM longterm_rental_contract WHERE rental_id = va.rental_id)),
  ends_at   = COALESCE((SELECT end_time   FROM hour_rental_detail WHERE rental_id = va.rental_id),
                       (SELECT end_time   FROM longterm_rental_contract WHERE rental_id = va.rental_id))
 WHERE va.starts_at IS NULL;

DO $$
DECLARE bad INTEGER;
BEGIN
  SELECT count(*) INTO bad FROM vehicle_assignments
   WHERE status = 'assigned' AND driver_staff_id IS NOT NULL AND (starts_at IS NULL OR ends_at IS NULL OR ends_at <= starts_at);
  IF bad > 0 THEN RAISE EXCEPTION '% bản ghi assigned có tài xế nhưng thiếu khoảng giờ hợp lệ', bad; END IF;
END $$;

-- 4. Generated period column and exclusion constraint
ALTER TABLE vehicle_assignments ADD COLUMN IF NOT EXISTS period tsrange
  GENERATED ALWAYS AS (CASE WHEN starts_at IS NOT NULL AND ends_at IS NOT NULL AND ends_at > starts_at
                            THEN tsrange(starts_at, ends_at + make_interval(mins => buffer_minutes), '[)') END) STORED;

DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_constraint WHERE conname = 'ex_va_driver_no_overlap' AND conrelid = 'vehicle_assignments'::regclass
  ) THEN
    ALTER TABLE vehicle_assignments ADD CONSTRAINT ex_va_driver_no_overlap
      EXCLUDE USING gist (driver_staff_id WITH =, period WITH &&)
      WHERE (status = 'assigned' AND driver_staff_id IS NOT NULL AND period IS NOT NULL);
  END IF;
END $$;

-- 5. Performance indexes
CREATE INDEX IF NOT EXISTS ix_va_driver_status ON vehicle_assignments (driver_staff_id, status);
CREATE INDEX IF NOT EXISTS ix_va_vehicle ON vehicle_assignments (vehicle_id, status);
