ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS allowed_license_types text[] NOT NULL DEFAULT '{}';

CREATE TABLE IF NOT EXISTS customer_licenses (
    id serial PRIMARY KEY,
    customer_id integer NOT NULL,
    number varchar(255) NOT NULL,
    type varchar(255) NOT NULL,
    issue_date date,
    expiry_date date,
    image_url text,
    status varchar(20) NOT NULL DEFAULT 'pending' CHECK (status IN ('pending','approved','rejected')),
    UNIQUE(customer_id, number)
);
INSERT INTO customer_licenses(customer_id,number,type,issue_date,expiry_date,image_url,status)
SELECT id,btrim(license_number),upper(btrim(COALESCE(license_type,''))),license_issue_date,
       license_expiry_date,license_img,COALESCE(license_status,'pending')
FROM customers WHERE NULLIF(btrim(license_number),'') IS NOT NULL
ON CONFLICT(customer_id,number) DO NOTHING;

-- Legacy columns remain a compatibility projection. Also read legacy records created
-- by older clients/seeds without copying them on a read.
CREATE OR REPLACE VIEW customer_license_records AS
SELECT id,customer_id,number,type,issue_date,expiry_date,image_url,status FROM customer_licenses
UNION ALL
SELECT NULL::integer,c.id,btrim(c.license_number),upper(btrim(COALESCE(c.license_type,''))),
       c.license_issue_date,c.license_expiry_date,c.license_img,COALESCE(c.license_status,'pending')
FROM customers c WHERE NULLIF(btrim(c.license_number),'') IS NOT NULL
AND NOT EXISTS(SELECT 1 FROM customer_licenses l WHERE l.customer_id=c.id AND l.number=btrim(c.license_number));
