INSERT INTO roles(name)
SELECT 'CustomerService' WHERE NOT EXISTS (SELECT 1 FROM roles WHERE name='CustomerService');

ALTER TABLE users ADD COLUMN authorization_version INTEGER NOT NULL DEFAULT 0;

CREATE FUNCTION bump_user_authorization() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP <> 'INSERT' THEN
        UPDATE users SET authorization_version=authorization_version+1 WHERE id=OLD.user_id;
    END IF;
    IF TG_OP <> 'DELETE' THEN
        IF TG_OP = 'INSERT' THEN
            UPDATE users SET authorization_version=authorization_version+1 WHERE id=NEW.user_id;
        ELSIF NEW.user_id IS DISTINCT FROM OLD.user_id THEN
            UPDATE users SET authorization_version=authorization_version+1 WHERE id=NEW.user_id;
        END IF;
    END IF;
    RETURN NULL;
END;
$$;
CREATE TRIGGER trg_role_authorization AFTER INSERT OR UPDATE OR DELETE ON user_roles
FOR EACH ROW EXECUTE FUNCTION bump_user_authorization();
CREATE TRIGGER trg_staff_authorization AFTER INSERT OR UPDATE OR DELETE ON staffs
FOR EACH ROW EXECUTE FUNCTION bump_user_authorization();

CREATE FUNCTION bump_account_authorization() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.is_active IS DISTINCT FROM OLD.is_active OR NEW.password_hash IS DISTINCT FROM OLD.password_hash THEN
        NEW.authorization_version := OLD.authorization_version+1;
    END IF;
    RETURN NEW;
END;
$$;
CREATE TRIGGER trg_account_authorization BEFORE UPDATE ON users
FOR EACH ROW EXECUTE FUNCTION bump_account_authorization();
