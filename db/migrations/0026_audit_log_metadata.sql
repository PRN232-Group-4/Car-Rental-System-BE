ALTER TABLE user_administration_audit
    ADD COLUMN ip_address VARCHAR(45),
    ADD COLUMN status VARCHAR(20) NOT NULL DEFAULT 'success';

CREATE INDEX ix_user_administration_audit_created ON user_administration_audit(created_at DESC, id DESC);
