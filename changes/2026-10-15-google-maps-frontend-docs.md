# Đồng bộ hướng dẫn Maps sau tích hợp frontend

- Cập nhật `docs/google-maps-setup.md`: FE đã tích hợp Maps Embed cho cơ sở/điểm nhận và gọi API tracking để xem vị trí xe, lịch sử đơn thuê.
- Phân biệt `GoogleMaps__ApiKey` của backend với `NEXT_PUBLIC_GOOGLE_MAPS_EMBED_API_KEY` tại môi trường build frontend; liên kết tới tài liệu cấu hình FE.
- Không đổi API/backend, hạ tầng hay biến môi trường đang triển khai. Giới hạn Legacy của provider vẫn như tài liệu trước. Không kiểm chứng key/billing/Google hoặc Railway thật.
- Đối chiếu endpoint theo controller/DTO hiện có; kiểm thử API giả lập và build thực hiện tại frontend. Không chạy lại backend tests vì chỉ sửa tài liệu.
- Liên quan: NFR-002 (hướng dẫn cấu hình triển khai).
