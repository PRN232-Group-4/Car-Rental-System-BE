-- Keep the authenticated operator for each driver assignment or reassignment.
ALTER TABLE vehicle_assignments
    ADD COLUMN IF NOT EXISTS assigned_by VARCHAR(255);
