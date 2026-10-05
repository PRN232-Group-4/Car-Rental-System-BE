-- Existing installations may already have these columns added manually.
ALTER TABLE vehicle_types ADD COLUMN IF NOT EXISTS type VARCHAR(100);
ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS license_plate VARCHAR(255);
-- Do not guess categories from display names. Configure existing vehicle_types
-- with type car/motorbike and fuel_type gasoline/electric before editing vehicles.
