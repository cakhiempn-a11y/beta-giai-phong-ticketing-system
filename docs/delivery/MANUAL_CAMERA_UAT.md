# QUY TRÌNH NGHIỆM THU THỦ CÔNG CAMERA VẬT LÝ VÀ THIẾT BỊ QUÉT QR (`MANUAL_CAMERA_UAT`)

**Đề tài:** Phân tích, thiết kế và xây dựng hệ thống quản lý bán vé trực tuyến tại rạp chiếu phim Beta Giải Phóng  
**Tác giả:** Vũ Văn Khiêm (`11235581`) — Ngành Hệ thống thông tin quản lý (MIS)  
**Giảng viên hướng dẫn:** ThS. Nguyễn Anh Phương  
**Trạng thái hiện tại trong môi trường CI / Tự động hóa:** **`MANUAL UAT — NOT TESTED`**

---

## 1. Mục đích và Phạm vi Tài liệu

Trong hệ thống kiểm thử tự động (xUnit và Playwright Chromium E2E), chức năng soát vé QR tại trang `/staff/check-in` đã được kiểm chứng tự động ở hai chế độ:
1. **Tải tệp ảnh QR PNG trực tiếp lên trình duyệt (`#qrFileInput`):** Giải mã bằng thư viện `Html5Qrcode.scanFile()` kết hợp cơ chế multi-scale white-padded offscreen canvas fallback và gửi `POST /api/staff/tickets/scan` (**ĐẠT 100% PASS**).
2. **Nhập mã token thủ công (`#manualQrToken`):** Dành cho trường hợp màn hình điện thoại khách bị nứt vỡ hoặc dùng đầu đọc mã vạch 2D cổng USB (**ĐẠT 100% PASS**).

Tuy nhiên, việc thu hình trực tiếp qua **ống kính quang học của camera vật lý** (`navigator.mediaDevices.getUserMedia`) trên điện thoại di động thực tế hoặc webcam máy tính phụ thuộc vào phần cứng quang học, điều kiện ánh sáng phòng rạp và hộp thoại cấp quyền của hệ điều hành. Do đó, tài liệu này quy định quy trình nghiệm thu thủ công (Manual UAT Protocol) chuẩn hóa gồm **12 kịch bản thực địa** để thực hiện trước hoặc trong buổi bảo vệ đồ án.

---

## 2. Danh mục Thiết bị Mục tiêu cần Kiểm thử

| Mã thiết bị | Loại thiết bị & Trình duyệt | Vai trò trong kịch bản thực địa |
|---|---|---|
| **DEV-01** | Điện thoại Android (Chrome Mobile mới nhất) | Thiết bị của nhân viên soát vé (`STAFF`) sử dụng camera sau để quét mã QR. |
| **DEV-02** | Điện thoại iPhone (Safari iOS 16+) | Thiết bị của nhân viên soát vé (`STAFF`) hoặc thiết bị hiển thị vé điện tử của khách hàng (`CUSTOMER`). |
| **DEV-03** | Máy tính xách tay (Webcam tích hợp + Chrome/Edge Desktop) | Quầy soát vé cố định sử dụng webcam máy tính. |
| **DEV-04** | Đầu đọc mã vạch 2D cầm tay cổng USB (chế độ HID Keyboard Emulation — *tùy chọn*) | Quét mã QR tốc độ cao điền trực tiếp vào ô `#manualQrToken`. |

---

## 3. Yêu cầu Mạng và Bảo mật HTTPS cho WebRTC Camera API (`getUserMedia`)

Theo tiêu chuẩn bảo mật của trình duyệt hiện đại (W3C Secure Contexts), API `navigator.mediaDevices.getUserMedia` **chỉ hoạt động** trên ngữ cảnh an toàn (`https://...` hoặc `http://localhost`). Khi truy cập từ điện thoại di động qua địa chỉ IP mạng LAN (`http://192.168.x.x:5000`), trình duyệt sẽ chặn quyền truy cập Camera nếu không dùng HTTPS hoặc Port Forwarding.

### Phương án A (Khuyên dùng cho Android): USB Port Forwarding qua Chrome DevTools
1. Cắm cáp USB kết nối điện thoại Android với máy tính chạy `BetaGiaiPhong.Web`.
2. Trên máy tính, mở `chrome://inspect/#devices`, bật `Discover USB devices` và cấu hình **Port forwarding**: cổng `5145` trên điện thoại ánh xạ về `localhost:5145` của máy tính.
3. Trên điện thoại Android, mở `http://localhost:5145/staff/check-in` — trình duyệt coi `localhost` là ngữ cảnh an toàn và cho phép bật Camera ngay lập tức mà không gặp cảnh báo chứng chỉ tự ký.

### Phương án B (Dùng cho cả Android, iPhone và Laptop): Khởi chạy Kestrel HTTPS
1. Tin cậy chứng chỉ HTTPS phát triển trên máy trạm:
   ```powershell
   dotnet dev-certs https --trust
   ```
2. Khởi chạy ứng dụng với cấu hình HTTPS lắng nghe trên mạng LAN:
   ```powershell
   dotnet run --project src/BetaGiaiPhong.Web/BetaGiaiPhong.Web.csproj --launch-profile https
   ```
3. Truy cập `https://<IP-LAN-Máy-Tính>:7097/staff/check-in` từ trình duyệt điện thoại và chấp nhận cảnh báo chứng chỉ phát triển nội bộ.

---

## 4. Bảng 12 Kịch bản Nghiệm thu Thủ công Camera & Thiết bị (`UAT-CAM-01` .. `UAT-CAM-12`)

| Mã kịch bản | Tên kịch bản kiểm thử | Thiết bị | Các bước thực hiện | Kết quả kỳ vọng | Trạng thái hiện tại |
|---|---|---|---|---|:---:|
| **`UAT-CAM-01`** | Cấp quyền truy cập Camera lần đầu (`Grant Permission`) | DEV-01, DEV-02, DEV-03 | 1. Đăng nhập tài khoản `staff@demo.invalid`.<br/>2. Mở `/staff/check-in`, chọn suất chiếu đang mở.<br/>3. Nhấn nút **Bật Camera** (`#btnStartCamera`).<br/>4. Chọn **Allow / Cho phép** trên hộp thoại của trình duyệt. | Khung hình video trực tiếp từ camera hiển thị mượt trong vùng `#reader`; nút **Dừng Camera** (`#btnStopCamera`) được kích hoạt. | **`NOT TESTED`** |
| **`UAT-CAM-02`** | Từ chối quyền truy cập Camera (`Deny Permission`) & Chuyển sang phương án dự phòng | DEV-01, DEV-02, DEV-03 | 1. Mở `/staff/check-in` trong phiên ẩn danh mới.<br/>2. Nhấn **Bật Camera** và chọn **Block / Từ chối** quyền camera.<br/>3. Chuyển sang tải tệp ảnh QR PNG vào `#qrFileInput`. | Ứng dụng không bị treo; hiển thị thông báo tiếng Việt hướng dẫn cấp quyền hoặc dùng tính năng tải ảnh QR / nhập mã thủ công; tải ảnh QR PNG vẫn soát vé thành công. | **`NOT TESTED`** |
| **`UAT-CAM-03`** | Chuyển đổi Camera trước / Camera sau (`Front / Rear Camera`) | DEV-01, DEV-02 | 1. Trên điện thoại có nhiều ống kính, kiểm tra chế độ mặc định (`facingMode: "environment"`).<br/>2. Thử dừng và khởi động lại camera. | Mặc định ưu tiên chọn **Camera sau** (`environment`) trên điện thoại di động để nhân viên dễ hướng ống kính về phía màn hình khách hàng. | **`NOT TESTED`** |
| **`UAT-CAM-04`** | Quét mã QR hợp lệ lần đầu (`ACCEPTED`) | DEV-01/02/03 quét màn hình khách | 1. Mở trang vé `/orders/{id}/tickets` của đơn `PAID` trên màn hình điện thoại khách.<br/>2. Đưa camera của nhân viên cách màn hình khách `15–20 cm`. | Nhận diện mã QR trong $\le 2$ giây; hiển thị huy hiệu màu xanh **`HỢP LỆ`** (`ACCEPTED`), thông tin ghế, mã vé; trạng thái vé trong DB chuyển sang `USED`. | **`NOT TESTED`** |
| **`UAT-CAM-05`** | Quét lại vé đã sử dụng (`ALREADY_USED`) | DEV-01/02/03 | Đưa lại cùng mã QR vừa quét ở `UAT-CAM-04` vào trước ống kính camera (hoặc nhấn nút **Quét lại vé này**). | Từ chối lượt vào; hiển thị huy hiệu cảnh báo **`ĐÃ SỬ DỤNG`** (`ALREADY_USED`) kèm thời điểm đã soát trước đó. | **`NOT TESTED`** |
| **`UAT-CAM-06`** | Quét vé hợp lệ nhưng chọn sai suất chiếu tại quầy (`WRONG_SHOWTIME`) | DEV-01/02/03 | 1. Trên màn hình `/staff/check-in`, chọn Suất chiếu B.<br/>2. Đưa mã QR của vé thuộc Suất chiếu A vào camera. | Từ chối lượt vào; hiển thị rõ **`SAI SUẤT CHIẾU`** (`WRONG_SHOWTIME`); vé gốc của Suất chiếu A vẫn giữ nguyên trạng thái `ISSUED` (không bị làm hỏng). | **`NOT TESTED`** |
| **`UAT-CAM-07`** | Quét vé ngoài khung giờ cho phép (`OUTSIDE_WINDOW`) | DEV-01/02/03 | Chọn một suất chiếu bắt đầu sau hơn 30 phút tính từ thời điểm hiện tại (`start_utc - 30m` chưa mở) và quét mã QR của suất đó. | Từ chối lượt vào; hiển thị **`NGOÀI GIỜ SOÁT VÉ`** (`OUTSIDE_WINDOW`); vé vẫn giữ nguyên trạng thái `ISSUED` để khách quay lại đúng giờ. | **`NOT TESTED`** |
| **`UAT-CAM-08`** | Quét mã QR giả mạo / không tồn tại (`NOT_FOUND`) | DEV-01/02/03 | Tạo một mã QR bất kỳ bên ngoài (ví dụ chứa chuỗi `FAKE_TOKEN_123456`) và đưa vào camera của nhân viên. | Từ chối lượt vào; hiển thị **`KHÔNG TÌM THẤY VÉ`** (`NOT_FOUND`) hoặc **`MÃ QR KHÔNG HỢP LỆ`**; ghi nhận sự kiện từ chối vào `AuditLogs`. | **`NOT TESTED`** |
| **`UAT-CAM-09`** | Quét mã QR khi màn hình điện thoại khách để độ sáng thấp (`< 25% Brightness`) | DEV-01/02 | Giảm độ sáng màn hình điện thoại khách hàng xuống dưới 25% trong điều kiện ánh sáng phòng chờ rạp phim và đưa vào camera. | Nhờ nền trắng quiet-zone chuẩn của ảnh PNG `246x246` px và mức sửa lỗi `ECCLevel.M`, camera vẫn lấy nét và giải mã thành công. | **`NOT TESTED`** |
| **`UAT-CAM-10`** | Quét mã QR ở góc nghiêng $30^\circ$ hoặc có bóng lóa kính cường lực (`Angle & Glare`) | DEV-01/02 | Nghiêng màn hình điện thoại khách khoảng $30^\circ$ so với trục ống kính camera nhân viên. | Ba ô định vị góc (Finder Patterns) của mã QR giúp thuật toán hiệu chỉnh phối cảnh và giải mã chính xác mà không báo mã sai. | **`NOT TESTED`** |
| **`UAT-CAM-11`** | Chống gửi lặp liên tục khi giữ nguyên mã QR trước ống kính (`Rapid Double-Scan Debounce`) | DEV-01/02/03 | Giữ nguyên màn hình mã QR hợp lệ trước ống kính camera liên tục trong 3 giây. | Giao diện khóa cờ `isProcessingScan` trong lúc đang gọi API và tạm dừng quét lặp cùng một chuỗi ngay lập tức, không bắn hàng chục request trùng nhau lên máy chủ. | **`NOT TESTED`** |
| **`UAT-CAM-12`** | Đứt kết nối mạng tạm thời trong lúc quét (`Network Loss & Retry`) | DEV-01/02/03 | 1. Tắt Wi-Fi trên thiết bị nhân viên (hoặc bật chế độ Offline) ngay trước khi quét.<br/>2. Quét mã QR $\rightarrow$ quan sát thông báo lỗi.<br/>3. Bật lại Wi-Fi và nhấn nút **Thử lại kết nối**. | Khi mất mạng, hiển thị **`LỖI KẾT NỐI`** kèm nút **Thử lại kết nối**; sau khi bật lại mạng và bấm thử lại, vé được xác thực thành công. | **`NOT TESTED`** |

---

## 5. Mẫu Biên bản Ký Nghiệm thu Thực địa (UAT Sign-off Sheet)

*(Dùng để ghi nhận kết quả khi thực hiện kiểm thử trên thiết bị thật trước buổi bảo vệ)*

- **Ngày thực hiện UAT:** `____ / ____ / 2026`
- **Người thực hiện:** Vũ Văn Khiêm (`11235581`)
- **Thiết bị đã dùng:** `[ ] Android Chrome` | `[ ] iPhone Safari` | `[ ] Laptop Webcam` | `[ ] USB 2D Scanner`
- **Tổng số kịch bản ĐẠT / Tổng số kịch bản thực hiện:** `____ / 12`
- **Nhận xét & Ghi chú thực địa:**  
  `.................................................................................................................`
