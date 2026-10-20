-- Operational metadata. Unknown historic values remain null/empty; never fabricate data.
ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS features text[] NOT NULL DEFAULT '{}';
ALTER TABLE facilities ADD COLUMN IF NOT EXISTS phone varchar(50);
