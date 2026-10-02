-- A rental has at most one active driver assignment. Historical assignments retain their non-active status.
WITH ranked AS (
    SELECT id, ROW_NUMBER() OVER (PARTITION BY rental_id ORDER BY assigned_at DESC, id DESC) AS row_number
    FROM vehicle_assignments
    WHERE status = 'assigned'
)
UPDATE vehicle_assignments
SET status = 'reassigned'
WHERE id IN (SELECT id FROM ranked WHERE row_number > 1);

CREATE UNIQUE INDEX IF NOT EXISTS ux_vehicle_assignments_active_rental
    ON vehicle_assignments (rental_id) WHERE status = 'assigned';
