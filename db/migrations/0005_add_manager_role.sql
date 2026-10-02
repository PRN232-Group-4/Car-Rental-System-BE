-- Manager may administer rental policies without receiving broader Admin rights.
INSERT INTO roles (name)
SELECT 'Manager'
WHERE NOT EXISTS (SELECT 1 FROM roles WHERE name = 'Manager');
