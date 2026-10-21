CREATE TABLE inspection_assignments (
    rental_id INTEGER NOT NULL,
    vehicle_id INTEGER NOT NULL,
    technician_staff_id INTEGER NOT NULL,
    assigned_at TIMESTAMP NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    assigned_by VARCHAR(255) NOT NULL,
    PRIMARY KEY (rental_id, vehicle_id)
);
CREATE INDEX ix_inspection_assignments_technician ON inspection_assignments(technician_staff_id, rental_id);
