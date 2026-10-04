-- Cột `code` đã được thêm thủ công vào database Neon trước đó; migration này để mọi môi trường
-- khác (CI, docker local, database mới) có cùng schema. IF NOT EXISTS nên chạy lại vô hại.
-- Không sửa docs/db/Init.sql: 0001 là baseline immutable (runner so checksum).
ALTER TABLE payments ADD COLUMN IF NOT EXISTS code VARCHAR(255);
