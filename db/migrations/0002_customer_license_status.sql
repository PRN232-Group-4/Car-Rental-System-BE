-- Run once on databases created from 0001 before deploying license-review APIs.
ALTER TABLE customers
    ADD COLUMN IF NOT EXISTS license_status VARCHAR(20) NOT NULL DEFAULT 'pending';

INSERT INTO roles (name)
SELECT role_name FROM (VALUES ('Customer'), ('Staff'), ('Admin'), ('Driver'), ('Maintenance')) AS seed(role_name)
WHERE NOT EXISTS (SELECT 1 FROM roles WHERE roles.name = seed.role_name);
