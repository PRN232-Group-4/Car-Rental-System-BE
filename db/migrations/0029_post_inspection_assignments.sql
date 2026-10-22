ALTER TABLE inspection_assignments
    ADD COLUMN inspection_type text NOT NULL DEFAULT 'pre_rental'
    CHECK (inspection_type IN ('pre_rental','post_rental'));
ALTER TABLE inspection_assignments DROP CONSTRAINT inspection_assignments_pkey;
ALTER TABLE inspection_assignments ADD PRIMARY KEY (rental_id,vehicle_id,inspection_type);
