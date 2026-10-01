-- Audit fields for refund approval. Apply after 0003.
ALTER TABLE payments ADD COLUMN IF NOT EXISTS reviewed_by VARCHAR(255);
ALTER TABLE payments ADD COLUMN IF NOT EXISTS reviewed_at TIMESTAMP;
