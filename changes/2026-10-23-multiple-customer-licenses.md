# Nhiều GPLX và cấu hình hạng phù hợp — 2026-10-23

Vehicle có allowedLicenseTypes trong create/update/list/detail/search. Danh sách chuẩn hóa trim/uppercase/dedup, tối đa 30 mã 1–20 chữ hoặc số. Mảng rỗng giữ hành vi xe cũ: bất kỳ bằng đã duyệt còn hạn. Không suy diễn thứ bậc hạng bằng.

Migration 0030 thêm customer_licenses, backfill dữ liệu cũ và view tương thích customer_license_records. Bằng có ID, được lưu/duyệt độc lập; PUT truyền LicenseId để sửa kể cả đổi số. Không có ID thì upsert theo số trong phạm vi khách hàng. GET customer/license và customer detail giữ contract cũ, thêm licenses aggregate. Không thêm API danh sách độc lập.

Booking (kể cả subscription), sửa đơn, phân công/đổi xe và bàn giao tự lái yêu cầu ít nhất một bằng approved đúng hạng xe và còn hạn đến hết kỳ thuê. Thuê có tài xế không yêu cầu bằng khách. Transaction khóa customer khi kiểm tra để phối hợp với submit/review. Bộ lọc duyệt và badge đếm mọi GPLX pending, không chỉ bằng hiện tại.

Kiểm thử: validator, HTTP multipart LicenseId/envelope nhiều bằng; integration Postgres cho lưu/sửa/duyệt, ownership, cấu hình xe và eligibility. Kết quả chạy ghi trong báo cáo bàn giao; không tự chạy migration vào DB đang phục vụ.

Xác minh cuối: build toàn bộ dependency và test project vào output riêng đạt 0 warning/0 error. 29 ca multi-license/handover + 104 ca upload/validation/vehicle/search/edit/lifecycle/assignment đạt trên PostgreSQL cổng 55439. Một test cũ Additional_evidence_uploads_use_resource_creation_roles(Manager,staff-license) lệch kỳ vọng 403 với policy Management hiện cho phép 200, không thuộc thay đổi GPLX khách. Không áp dụng migration lên DB ứng dụng đang chạy.
