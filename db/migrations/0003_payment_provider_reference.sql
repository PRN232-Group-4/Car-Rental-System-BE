-- Idempotency key for payment-provider webhooks. Apply after 0002.
ALTER TABLE payments ADD COLUMN IF NOT EXISTS provider_reference VARCHAR(255);
CREATE UNIQUE INDEX IF NOT EXISTS ux_payments_provider_reference
    ON payments (provider_reference) WHERE provider_reference IS NOT NULL;
