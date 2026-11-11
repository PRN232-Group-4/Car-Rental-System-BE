-- Khôi phục fn_immutable_row() về định nghĩa gốc (từ 0017_rental_lifecycle.sql)
CREATE OR REPLACE FUNCTION fn_immutable_row() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
  RAISE EXCEPTION '% là bảng chỉ ghi thêm (immutable)', TG_TABLE_NAME;
END $$;

-- Tạo hàm trigger riêng cho bảng customer_loyalty_transactions
CREATE OR REPLACE FUNCTION fn_loyalty_immutable_row() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
  RAISE EXCEPTION 'Bản ghi không được phép chỉnh sửa hoặc xóa' USING ERRCODE = '23505';
END $$;

-- Gắn trigger của customer_loyalty_transactions vào hàm riêng
DROP TRIGGER IF EXISTS trg_clt_immutable ON customer_loyalty_transactions;
CREATE TRIGGER trg_clt_immutable BEFORE UPDATE OR DELETE ON customer_loyalty_transactions
  FOR EACH ROW EXECUTE FUNCTION fn_loyalty_immutable_row();

DROP TRIGGER IF EXISTS trg_clt_no_truncate ON customer_loyalty_transactions;
CREATE TRIGGER trg_clt_no_truncate BEFORE TRUNCATE ON customer_loyalty_transactions
  FOR EACH STATEMENT EXECUTE FUNCTION fn_loyalty_immutable_row();
