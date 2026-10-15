-- Count rentals by their recorded policy reference without scanning all rentals per policy.
CREATE INDEX IF NOT EXISTS ix_rentals_policy_id ON rentals(policy_id) WHERE policy_id IS NOT NULL;
