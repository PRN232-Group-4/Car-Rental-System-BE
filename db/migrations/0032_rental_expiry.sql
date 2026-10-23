-- FR-042: yêu cầu đặt xe tự động hết hạn khi chưa được duyệt hoặc chưa thanh toán trong cửa sổ cho phép.
-- Cửa sổ mặc định 1 giờ; mốc hết hạn được lưu ở rentals.expire_at nên có thể đổi quy tắc
-- (ví dụ "hết hạn tại start_time - 1 giờ") mà không cần đổi schema.
-- Migration idempotent để chạy lại an toàn trong test.

-- 1. Trạng thái 'expired' cho rentals --------------------------------------------------------------
DO $$
DECLARE bad INTEGER;
BEGIN
  SELECT count(*) INTO bad FROM rentals
   WHERE status NOT IN ('pending','pre_inspection','renting','post_inspection','completed','cancelled','expired');
  IF bad > 0 THEN RAISE EXCEPTION 'rentals.status có % dòng ngoài tập hợp lệ', bad; END IF;
END $$;

ALTER TABLE rentals DROP CONSTRAINT IF EXISTS ck_rentals_status;
ALTER TABLE rentals ADD CONSTRAINT ck_rentals_status
  CHECK (status IN ('pending','pre_inspection','renting','post_inspection','completed','cancelled','expired'));

-- 2. Mốc hết hạn và thời điểm hết hạn thực tế -------------------------------------------------------
ALTER TABLE rentals
  ADD COLUMN IF NOT EXISTS expire_at  TIMESTAMP,
  ADD COLUMN IF NOT EXISTS expired_at TIMESTAMP;

COMMENT ON COLUMN rentals.expire_at IS
  'FR-042: hạn duyệt + thanh toán, tính từ created_at. NULL = không tự động hết hạn (dữ liệu trước tính năng).';
COMMENT ON COLUMN rentals.expired_at IS
  'FR-042: thời điểm job chuyển đơn sang trạng thái expired.';

CREATE INDEX IF NOT EXISTS ix_rentals_expire_at
  ON rentals (expire_at) WHERE status IN ('pending','pre_inspection');

-- 3. Backfill cho các yêu cầu đang mở ---------------------------------------------------------------
-- Đơn có trước khi triển khai nhận trọn một cửa sổ kể từ lúc deploy (GREATEST) để việc bật
-- tính năng không làm hết hạn hàng loạt đơn đang chờ của khách. Mọi đơn tạo sau migration
-- được ứng dụng đặt expire_at = thời điểm đặt + cửa sổ cấu hình.
UPDATE rentals
   SET expire_at = GREATEST(created_at + interval '1 hour', now() + interval '1 hour')
 WHERE expire_at IS NULL
   AND status IN ('pending','pre_inspection');

-- 4. 'expired' không giữ lịch xe ---------------------------------------------------------------------
-- fn_rental_is_blocking chỉ liệt kê 4 trạng thái đang hoạt động, nên khi trigger
-- trg_rentals_sync_booking thấy status='expired' nó tự đặt vehicle_bookings.is_blocking = false
-- và khung giờ đó được giải phóng cho khách khác. Khai báo lại để migration tự ghi rõ bất biến này.
CREATE OR REPLACE FUNCTION fn_rental_is_blocking(p_status TEXT) RETURNS BOOLEAN
LANGUAGE sql IMMUTABLE AS $$ SELECT p_status IN ('pending','pre_inspection','renting','post_inspection') $$;
