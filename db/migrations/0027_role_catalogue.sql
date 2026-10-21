-- Fail visibly on legacy duplicate names; never discard role/user membership data.
-- This also prevents case variants from shadowing a built-in authorization role.
CREATE UNIQUE INDEX ux_roles_name_ci ON roles(lower(name));
