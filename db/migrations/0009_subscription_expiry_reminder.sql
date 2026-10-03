-- Tracks whether the pre-expiry warning email was sent, so the FR-025 reminder job stays idempotent.
ALTER TABLE customer_subscriptions ADD COLUMN IF NOT EXISTS reminder_sent_at TIMESTAMP;
