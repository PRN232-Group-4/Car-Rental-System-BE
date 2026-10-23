CREATE TABLE IF NOT EXISTS rental_contract_documents (
    rental_id integer PRIMARY KEY,
    content bytea NOT NULL CHECK (octet_length(content) BETWEEN 1 AND 10485760),
    content_type varchar(32) NOT NULL CHECK (content_type IN ('application/pdf','image/jpeg','image/png','image/webp')),
    file_name varchar(80) NOT NULL,
    signed_at timestamp without time zone NOT NULL,
    uploaded_at timestamp without time zone NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    uploaded_by varchar(255) NOT NULL
);
