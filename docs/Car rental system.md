Car Rental App — Tài liệu Nghiệp vụ (Business Requirements)

Tài liệu này dùng để giao cho agent (AI hoặc thành viên team) thực hiện:

Thiết kế prototype trên Figma/Stitch
Thiết kế Database Conceptual Model

Agent cần tự đề xuất và quyết định xây dựng theo hướng hệ thống (system) hay nền tảng (platform) dựa trên phân tích nghiệp vụ bên dưới, có giải thích lý do lựa chọn.

1. Tổng quan

Xây dựng ứng dụng cho thuê xe với 2 hình thức thuê chính, kèm các tính năng quản lý vận hành đội xe (vị trí, bảo hành/bảo trì).

2. Các nhóm nghiệp vụ (Modules)
2.1. Thuê xe theo giờ
Khách hàng thuê xe theo khung giờ/ngày.
Có 2 lựa chọn:
Có tài xế: tài xế đi kèm cố định theo xe được chỉ định cho chuyến đó (không phải mô hình ghép chuyến/điều phối tài xế riêng lẻ như xe công nghệ — tài xế gắn với xe cụ thể).
Không có tài xế (tự lái): khách tự lái xe.
Cần xác định: cách tính giá theo giờ, xử lý gia hạn/trả trễ, trạng thái xe trong khung giờ đã đặt (tránh double-booking).
2.2. Thuê xe dài hạn
Hợp đồng thuê dài hạn (theo quý/năm).
Không kèm tài xế (khách tự vận hành xe trong suốt thời gian hợp đồng).
Cần xác định: quản lý vòng đời hợp đồng (tạo, gia hạn, chấm dứt), thanh toán định kỳ, bàn giao/thu hồi xe khi kết thúc hợp đồng.
2.3. Bản đồ quản lý vị trí xe
Xem trên map vị trí hiện tại của từng xe trong đội xe.
Dùng cho mục đích quản lý vận hành (biết xe đang ở đâu, xe nào đang rảnh/đang cho thuê).
Cần xác định: nguồn dữ liệu vị trí (GPS thiết bị gắn trên xe / định vị qua app tài xế), tần suất cập nhật, lịch sử di chuyển hay chỉ vị trí real-time.
2.4. Quản lý bảo hành/bảo trì xe
Theo dõi tình trạng an toàn kỹ thuật của xe.
Nhắc lịch bảo hành/bảo trì theo định kỳ hoặc theo mốc (km, thời gian).
2 hình thức xử lý khi đến hạn bảo hành:
Có người đến lấy xe đi bảo hành.
Chủ xe/nhân viên tự mang xe đi.
Cần xác định: trạng thái xe trong thời gian bảo hành (không khả dụng cho thuê), lịch sử bảo trì từng xe, cảnh báo xe không đạt điều kiện an toàn.
3. Vai trò người dùng (User Roles) — cần agent đề xuất chi tiết

Gợi ý các vai trò cần có trong hệ thống (agent rà soát và bổ sung nếu thiếu):

Khách hàng (thuê theo giờ / thuê dài hạn)
Tài xế (gắn với xe cụ thể cho các chuyến có tài)
Quản trị viên / Điều phối viên đội xe (quản lý xe, vị trí, bảo trì)
Nhân viên kỹ thuật/bảo trì
(Nếu build theo hướng nền tảng multi-chủ xe) Chủ xe / Đối tác cho thuê xe
4. Quyết định kiến trúc cần agent thực hiện

Agent cần tự quyết định và trình bày rõ lý do cho câu hỏi:

Hệ thống (system) hay Nền tảng (platform)?

Hệ thống: một đơn vị vận hành sở hữu và quản lý toàn bộ đội xe.
Nền tảng: nhiều chủ xe/đối tác tham gia đăng xe, nền tảng đóng vai trò trung gian kết nối.

Việc lựa chọn hướng này sẽ ảnh hưởng trực tiếp đến:

Số lượng vai trò (roles) trong hệ thống
Mô hình dữ liệu (có cần entity "Chủ xe/Đối tác" tách biệt hay không)
Luồng thanh toán (thanh toán trực tiếp cho đơn vị vận hành, hay có chia hoa hồng cho đối tác)
5. Yêu cầu Deliverables
5.1. Prototype (Figma / Stitch)
Thiết kế các màn hình chính cho từng module ở mục 2 (thuê theo giờ, thuê dài hạn, map vị trí xe, quản lý bảo hành/bảo trì).
Thiết kế theo từng vai trò người dùng liên quan (khách hàng, quản trị/điều phối, nhân viên kỹ thuật...).
Có luồng (flow) rõ ràng: từ tìm/chọn xe → đặt thuê → xác nhận/thanh toán → theo dõi trạng thái thuê/xe.
Trạng thái xe cần thể hiện trực quan trên UI (đang rảnh, đang cho thuê, đang bảo trì...).
5.2. Database Conceptual Model
Xác định các entity chính, ví dụ (agent cần rà soát và hoàn thiện):
Xe (Vehicle)
Khách hàng (Customer)
Tài xế (Driver)
Đơn thuê theo giờ (HourlyBooking)
Hợp đồng thuê dài hạn (LongTermContract)
Vị trí xe / Lịch sử vị trí (VehicleLocation)
Lịch bảo hành/bảo trì (MaintenanceSchedule/MaintenanceRecord)
(Tuỳ theo quyết định hệ thống/nền tảng) Chủ xe/Đối tác (Owner/Partner)
Xác định quan hệ giữa các entity và ràng buộc nghiệp vụ quan trọng, ví dụ:
Một xe không thể vừa được thuê theo giờ vừa đang trong trạng thái bảo trì cùng lúc.
Tài xế (nếu có) gắn 1-1 với xe trong 1 đơn thuê theo giờ có tài, không tách rời.
Hợp đồng dài hạn khoá xe khỏi pool cho thuê theo giờ trong suốt thời gian hợp đồng.
6. Công nghệ sử dụng (Tech Stack)
Backend: ASP.NET 8 (Web API)
Frontend: React
Database: PostgreSQL
Containerization: Docker
Source control / CI: GitHub