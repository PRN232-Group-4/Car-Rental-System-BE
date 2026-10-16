-- A provider checkout can exist before its webhook/payment row. Freeze rental terms first.
-- Older versions did not persist link issuance, so existing unpaid invoices cannot be
-- distinguished from invoices with an active external link. Freeze those once on upgrade.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'invoices' AND column_name = 'checkout_started_at') THEN
        ALTER TABLE invoices ADD COLUMN checkout_started_at TIMESTAMP;
        UPDATE invoices SET checkout_started_at = now() AT TIME ZONE 'utc' WHERE status = 'unpaid';
    END IF;
END $$;
