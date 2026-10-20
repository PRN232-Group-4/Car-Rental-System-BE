ALTER TABLE users ADD COLUMN address VARCHAR(500);

CREATE TABLE user_administration_audit (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    user_id INTEGER NOT NULL,
    actor_user_id INTEGER NOT NULL,
    action VARCHAR(40) NOT NULL,
    reason VARCHAR(1000),
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX ix_user_administration_audit_user ON user_administration_audit(user_id, id);
