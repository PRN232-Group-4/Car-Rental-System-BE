-- FR-010 / BE-10: single source of truth for vehicle availability.
-- All timestamps are UTC (timestamp without time zone). The migration is idempotent so it can be replayed in tests.

CREATE EXTENSION IF NOT EXISTS btree_gist;
CREATE EXTENSION IF NOT EXISTS pg_trgm;

-- 1. vehicles.status vocabulary ------------------------------------------------------------------
DO $$
DECLARE bad INTEGER;
BEGIN
  SELECT count(*) INTO bad FROM vehicles
   WHERE status IS NULL OR status NOT IN ('available','renting','maintenance','inactive');
  IF bad > 0 THEN
    RAISE EXCEPTION 'vehicles.status has % rows outside the valid set (available, renting, maintenance, inactive); fix the data before running 0011', bad;
  END IF;
END $$;

ALTER TABLE vehicles ALTER COLUMN status SET NOT NULL;

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_vehicles_status' AND conrelid = 'vehicles'::regclass) THEN
    ALTER TABLE vehicles ADD CONSTRAINT ck_vehicles_status
      CHECK (status IN ('available','renting','maintenance','inactive'));
  END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_vehicles_search ON vehicles (vehicle_type_id, facility_id)
  WHERE status IN ('available','renting');
CREATE INDEX IF NOT EXISTS ix_vehicles_name_trgm ON vehicles
  USING gin ((lower(coalesce(brand,'') || ' ' || coalesce(model,''))) gin_trgm_ops);
CREATE INDEX IF NOT EXISTS ix_vehicle_maintenance_vehicle ON vehicle_maintenance (vehicle_id);

-- 2. vehicle_bookings ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS vehicle_bookings (
  rental_id       INTEGER PRIMARY KEY,                     -- -> rentals.id (one row per rental)
  vehicle_id      INTEGER NOT NULL,                        -- -> vehicles.id (current vehicle)
  starts_at       TIMESTAMP NOT NULL,                      -- UTC
  ends_at         TIMESTAMP NOT NULL,                      -- UTC
  buffer_minutes  INTEGER   NOT NULL DEFAULT 60,
  is_blocking     BOOLEAN   NOT NULL DEFAULT TRUE,
  period          tsrange GENERATED ALWAYS AS
                    (tsrange(starts_at, ends_at + make_interval(mins => buffer_minutes), '[)')) STORED,
  created_at      TIMESTAMP NOT NULL DEFAULT now(),
  CONSTRAINT ck_vb_range  CHECK (ends_at > starts_at),
  CONSTRAINT ck_vb_buffer CHECK (buffer_minutes >= 0),
  CONSTRAINT ex_vehicle_bookings_no_overlap
    EXCLUDE USING gist (vehicle_id WITH =, period WITH &&) WHERE (is_blocking)
);

CREATE OR REPLACE FUNCTION fn_rental_is_blocking(p_status TEXT) RETURNS BOOLEAN
LANGUAGE sql IMMUTABLE AS $$ SELECT p_status IN ('pending','pre_inspection','renting','post_inspection') $$;

CREATE OR REPLACE FUNCTION fn_sync_vehicle_booking_from_detail() RETURNS trigger
LANGUAGE plpgsql AS $$
DECLARE r RECORD;
BEGIN
  SELECT id, vehicle_id, status INTO r FROM rentals WHERE id = NEW.rental_id;
  IF NOT FOUND THEN RAISE EXCEPTION 'Rental % does not exist', NEW.rental_id; END IF;
  IF NEW.start_time IS NULL OR NEW.end_time IS NULL OR NEW.end_time <= NEW.start_time THEN
    RETURN NEW;  -- incomplete schedule: the application validates first, no booking is created
  END IF;
  INSERT INTO vehicle_bookings (rental_id, vehicle_id, starts_at, ends_at, is_blocking)
  VALUES (r.id, r.vehicle_id, NEW.start_time, NEW.end_time, fn_rental_is_blocking(r.status))
  ON CONFLICT (rental_id) DO UPDATE
    SET vehicle_id = EXCLUDED.vehicle_id, starts_at = EXCLUDED.starts_at,
        ends_at = EXCLUDED.ends_at, is_blocking = EXCLUDED.is_blocking;
  RETURN NEW;
END $$;

DROP TRIGGER IF EXISTS trg_hour_detail_sync_booking ON hour_rental_detail;
CREATE TRIGGER trg_hour_detail_sync_booking
  AFTER INSERT OR UPDATE OF start_time, end_time ON hour_rental_detail
  FOR EACH ROW EXECUTE FUNCTION fn_sync_vehicle_booking_from_detail();
DROP TRIGGER IF EXISTS trg_longterm_sync_booking ON longterm_rental_contract;
CREATE TRIGGER trg_longterm_sync_booking
  AFTER INSERT OR UPDATE OF start_time, end_time ON longterm_rental_contract
  FOR EACH ROW EXECUTE FUNCTION fn_sync_vehicle_booking_from_detail();

CREATE OR REPLACE FUNCTION fn_sync_vehicle_booking_from_rental() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
  IF NEW.vehicle_id IS DISTINCT FROM OLD.vehicle_id THEN
    -- vehicle swap mid-rental: only reserve the remaining part so the new vehicle's past schedule does not conflict
    UPDATE vehicle_bookings
       SET vehicle_id  = NEW.vehicle_id,
           starts_at   = LEAST(ends_at - interval '1 minute', GREATEST(starts_at, (now() AT TIME ZONE 'utc'))),
           is_blocking = fn_rental_is_blocking(NEW.status)
     WHERE rental_id = NEW.id;
  ELSIF NEW.status IS DISTINCT FROM OLD.status THEN
    UPDATE vehicle_bookings SET is_blocking = fn_rental_is_blocking(NEW.status) WHERE rental_id = NEW.id;
  END IF;
  RETURN NEW;
END $$;

DROP TRIGGER IF EXISTS trg_rentals_sync_booking ON rentals;
CREATE TRIGGER trg_rentals_sync_booking
  AFTER UPDATE OF vehicle_id, status ON rentals
  FOR EACH ROW EXECUTE FUNCTION fn_sync_vehicle_booking_from_rental();

-- 3. maintenance blocks --------------------------------------------------------------------------
CREATE OR REPLACE VIEW v_vehicle_maintenance_blocks AS
SELECT m.id AS maintenance_id, m.vehicle_id,
       tsrange(
         COALESCE(m.start_time, m.scheduled_date::timestamp - interval '7 hours', '-infinity'::timestamp),
         CASE
           WHEN m.status = 'completed' THEN 'infinity'::timestamp          -- not is_safe yet: keep blocking
           ELSE COALESCE(m.end_time,
                  CASE WHEN m.status = 'scheduled' AND m.start_time IS NULL AND m.scheduled_date IS NOT NULL
                       THEN m.scheduled_date::timestamp - interval '7 hours' + interval '1 day' END,
                  'infinity'::timestamp)
         END, '[)') AS blocked_range
  FROM vehicle_maintenance m
 WHERE m.status IN ('open','scheduled','in_progress')
    OR (m.status = 'completed' AND COALESCE(m.is_safe, FALSE) = FALSE);

-- 4. backfill from existing rentals (oldest first; overlapping legacy rows are skipped and reported) ---
DO $$
DECLARE blocking_total INTEGER; missing INTEGER;
BEGIN
  INSERT INTO vehicle_bookings (rental_id, vehicle_id, starts_at, ends_at, is_blocking)
  SELECT r.id, r.vehicle_id, d.start_time, d.end_time, fn_rental_is_blocking(r.status)
    FROM rentals r
    JOIN (SELECT rental_id, start_time, end_time FROM hour_rental_detail
          UNION ALL
          SELECT rental_id, start_time, end_time FROM longterm_rental_contract) d ON d.rental_id = r.id
   WHERE d.start_time IS NOT NULL AND d.end_time IS NOT NULL AND d.end_time > d.start_time
   ORDER BY r.created_at, r.id
  ON CONFLICT DO NOTHING;

  SELECT count(*) INTO blocking_total FROM rentals WHERE fn_rental_is_blocking(status);
  SELECT count(*) INTO missing FROM rentals r
   WHERE fn_rental_is_blocking(r.status)
     AND NOT EXISTS (SELECT 1 FROM vehicle_bookings b WHERE b.rental_id = r.id AND b.is_blocking);
  RAISE NOTICE '0011 backfill: % blocking rentals, % without a blocking booking (overlap or missing schedule; fix manually)', blocking_total, missing;
END $$;
