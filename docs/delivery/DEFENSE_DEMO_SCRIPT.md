# KỊCH BẢN TRÌNH DIỄN BẢO VỆ ĐỒ ÁN TỐT NGHIỆP (12–15 PHÚT) & PHƯƠNG ÁN DỰ PHÒNG (`DEFENSE_DEMO_SCRIPT`)

**Đề tài:** Phân tích, thiết kế và xây dựng hệ thống quản lý bán vé trực tuyến tại rạp chiếu phim Beta Giải Phóng  
**Sinh viên trình bày:** Vũ Văn Khiêm (`11235581`) — Ngành Hệ thống thông tin quản lý (MIS)  
**Giảng viên hướng dẫn:** ThS. Nguyễn Anh Phương  
**Phiên bản:** 1.1 (RC-01 Reconciled)  
**Thời lượng thiết kế:** 2 chế độ rõ ràng — **Chế độ 1: Full Journey Demo (12–15 phút)** và **Chế độ 2: Quick Recovery / Short Demo (5 phút)**

---

## 1. Danh mục Chuẩn bị Trước Giờ Bảo vệ (T-15 Phút) & Hai Chế độ Khởi tạo

Thực hiện tại máy tính cá nhân trước khi vào phòng bảo vệ 15 phút. Tùy theo thời lượng Hội đồng cho phép, chọn **một trong hai chế độ khởi tạo** dưới đây để không xảy ra xung đột lịch chiếu tại Phòng 01 (`P01`):

### Chế độ 1: Full Journey Demo (12–15 Phút — Khuyên dùng cho bài trình bày chính)
- **Mục đích:** Trình diễn trọn vẹn từ lúc `MANAGER` tạo phim mới, tự xếp lịch chiếu mới ở **Phòng 01 (`P01`)** tại thời điểm **`hiện tại + 15 phút`**, `CUSTOMER` đặt ghế & thanh toán, `STAFF` quét ảnh QR đến `MANAGER` xem báo cáo doanh thu.
- **Lệnh chuẩn bị (Không chạy `seed-checkin-demo` để giữ trống khung giờ `hiện tại + 15 phút` của Phòng 01):**
  ```powershell
  cd D:\Projects\BetaGiaiPhong
  $env:BGP_DEMO_PASSWORD = "BetaDemo@2026!"
  . .\scripts\01_prepare_dev_session.ps1
  dotnet run --project .\src\BetaGiaiPhong.Runner -- migrate
  dotnet run --project .\src\BetaGiaiPhong.Runner -- seed-demo
  dotnet run --project .\src\BetaGiaiPhong.Runner -- verify
  dotnet run --project .\src\BetaGiaiPhong.Web --launch-profile http
  ```

### Chế độ 2: Quick Recovery / Short Demo (5 Phút — Dùng khi Hội đồng yêu cầu đi thẳng vào đặt ghế & quét vé)
- **Mục đích:** Tạo sẵn 1 suất chiếu ở Phòng 01 tại **`hiện tại + 15 phút`** (nằm ngay trong cửa sổ soát vé `-30m..+15m`) và 1 vé `PAID` sẵn sàng để quét ngay lập tức mà không cần tạo phim/suất chiếu thủ công.
- **Lệnh chuẩn bị (Có chạy `seed-checkin-demo`):**
  ```powershell
  cd D:\Projects\BetaGiaiPhong
  $env:BGP_DEMO_PASSWORD = "BetaDemo@2026!"
  . .\scripts\01_prepare_dev_session.ps1
  dotnet run --project .\src\BetaGiaiPhong.Runner -- migrate
  dotnet run --project .\src\BetaGiaiPhong.Runner -- seed-demo
  dotnet run --project .\src\BetaGiaiPhong.Runner -- seed-checkin-demo
  dotnet run --project .\src\BetaGiaiPhong.Runner -- verify
  dotnet run --project .\src\BetaGiaiPhong.Web --launch-profile http
  ```
  > **Lưu ý chống đụng độ lịch chiếu Phòng 01:** Lệnh `seed-checkin-demo` tạo một suất chiếu ở **Phòng 01 (`P01`)** bắt đầu tại `UTC hiện tại + 15 phút`. Nếu đã chạy `seed-checkin-demo` mà vẫn muốn thử tạo thêm suất chiếu mới tại `hiện tại + 15 phút`, hãy chọn **Phòng chiếu số 2 (`P02`)** (hoặc kiểm tra trước danh sách lịch chiếu tại `/management/showtimes`).

### Kiểm tra nhanh trước khi trình chiếu (Pre-Demo Check)
1. **Kiểm tra cổng Web:** Mở `http://localhost:5145` và xác nhận trang chủ hiển thị danh sách phim.
2. **Mở sẵn 3 cửa sổ trình duyệt độc lập (để không ghi đè Cookie `.BetaGiaiPhong.Auth` của nhau):**
   - **Cửa sổ 1 (Profile Bình thường — Vai trò `MANAGER`):** Đăng nhập sẵn `manager@demo.invalid` (`BetaDemo@2026!`), mở `/management/showtimes` để kiểm tra nhanh các khung giờ hiện có của Phòng 01 và Phòng 02.
   - **Cửa sổ 2 (Cửa sổ Ẩn danh / InPrivate — Vai trò `CUSTOMER`):** Đăng nhập sẵn `customer1@demo.invalid` (`BetaDemo@2026!`), mở `/movies`.
   - **Cửa sổ 3 (Profile thứ hai hoặc trình duyệt khác — Vai trò `STAFF`):** Đăng nhập sẵn `staff@demo.invalid` (`BetaDemo@2026!`), mở `/staff/check-in`.
3. **Mở sẵn cửa sổ PowerShell thứ hai (đã chạy `. .\scripts\01_prepare_dev_session.ps1`):** Sẵn sàng chạy lệnh `dotnet run --project .\src\BetaGiaiPhong.Runner -- verify` ở cuối buổi bảo vệ.

---

## 2. Kịch bản Trình diễn Trực tiếp 14 Bước (12–15 Phút — Chế độ 1)

| Bước | Thời lượng | Cửa sổ / Vai trò | Thao tác thực hiện trực tiếp trên `http://localhost:5145` | Điểm nhấn kỹ thuật thuyết minh với Hội đồng |
|:---:|:---:|---|---|---|
| **1** | `01:00` | Slide / ERD | Giới thiệu nhanh kiến trúc Modular Monolith (.NET 10 + EF Core 10 + SQL Server 2022) và sơ đồ ERD 17 bảng, 22 khóa ngoại `NO ACTION`. | Nhấn mạnh hệ thống giải quyết 4 bài toán lõi: **chống bán trùng ghế**, **thu hồi ghế quá hạn**, **webhook lũy đẳng/thanh toán muộn (`CR-01`)** và **vé QR mật mã (`AES-256-GCM` + `SHA-256`)**. |
| **2** | `01:00` | Cửa sổ 1 (`MANAGER`) | Vào `/management/movies/create`, tạo phim mới ở trạng thái `DRAFT`. Sang Cửa sổ 2 (`CUSTOMER`) tải lại `/movies` để chứng minh phim `DRAFT` chưa hiện. Quay lại Cửa sổ 1 nhấn **Công bố (`POST /management/movies/{id}/publish`)**. | Chứng minh quy tắc `FR-CAT-02`: chỉ phim đã công bố (`PUBLISHED`) mới xuất hiện trên cổng khách hàng và mới được xếp lịch chiếu. |
| **3** | `01:30` | Cửa sổ 1 (`MANAGER`) | Vào `/management/showtimes` kiểm tra khung giờ trống của phòng chiếu; vào `/management/showtimes/create`, chọn phim vừa Publish, chọn Phòng 01 (hoặc Phòng 02), đặt giờ chiếu **`hiện tại + 15 phút`** (nằm ngay trong cửa sổ soát vé `-30m..+15m`), giá vé `75.000đ / 95.000đ / 180.000đ`, nhấn **Công bố**. Sau đó thử tạo thêm 1 suất chiếu trùng giờ trong cùng phòng đó. | Hệ thống mở bán suất đầu tiên (`ON_SALE`), đóng băng `sellable_capacity_snapshot` và sinh toàn bộ ghế `ShowtimeSeats`. Ở lần tạo trùng giờ thứ hai, hệ thống **từ chối ngay lập tức** (có tính cả 15 phút đệm dọn phòng `BR-02`). |
| **4** | `00:45` | Cửa sổ 2 (`CUSTOMER`) | Tải lại `/movies`, lọc phim vừa tạo, nhấn vào suất chiếu vừa mở bán để mở sơ đồ ghế `/showtimes/{id}/seats`. | Sơ đồ ghế hiển thị phân biệt rõ ghế Thường, VIP, Đôi và trạng thái thời gian thực (`AVAILABLE`, `HELD`, `SOLD`). |
| **5** | `01:00` | Cửa sổ 2 (`CUSTOMER`) | Thử click chọn 9 ghế liên tiếp $\rightarrow$ UI chặn ở ghế thứ 8 (`BR-04`). Giữ lại 2 ghế (`A1` Thường và `C1` VIP), tổng tiền `170.000 đ`. | Giới hạn tối đa 8 ghế/đơn được kiểm soát ở cả giao diện, Domain Service và ràng buộc `CK_Orders_02` trong SQL Server. |
| **6** | `01:00` | Cửa sổ 2 (`CUSTOMER`) | Nhấn **Đặt vé** (`POST /bookings/reserve`) $\rightarrow$ tạo đơn `PENDING_PAYMENT` và chuyển tới `/orders/{id}/checkout` với đồng hồ đếm ngược 5 phút. Mở một tab khác xem lại sơ đồ ghế `/showtimes/{id}/seats`: 2 ghế vừa chọn đã chuyển sang trạng thái **`Đang giữ (HELD)`**; nếu khách khác cố đặt cùng ghế sẽ nhận thông báo xung đột `409 Conflict`. | Giải thích cơ chế giao dịch `IsolationLevel.Serializable` kết hợp khóa `WITH (UPDLOCK, HOLDLOCK)` trên dòng `Showtimes`, đảm bảo tính nguyên tử *All-or-Nothing*. |
| **7** | `01:00` | Cửa sổ 2 (`CUSTOMER`) | Trên trang `/orders/{id}/checkout`, nhấn nút **Thanh toán Thành công (`POST /orders/{id}/demo-pay`)**. | Hệ thống tạo bản ghi `Payments`, xử lý callback ký số `HMAC-SHA256`, chuyển đơn sang `PAID`, chuyển ghế sang `SOLD` và phát hành 2 vé điện tử QR trong cùng 1 transaction. |
| **8** | `00:45` | Cửa sổ 2 (`CUSTOMER`) | Trên trang `/orders/{id}/tickets`, trình bày 2 thẻ vé điện tử (`ISSUED`). Nhấn chuột phải vào ảnh QR (`/tickets/{ticketId}/qr`) của ghế `A1` chọn **Lưu hình ảnh thành... (`ticket-A1.png`)**. | Giải thích `CR-06`: CSDL không lưu chuỗi `qr_token` dạng rõ mà chỉ lưu băm `SHA-256` và bản mã `AES-256-GCM`; ảnh PNG `259x259` px (`ECCLevel.M`, `GetGraphic(7)`) được giải mã trực tiếp trong RAM. |
| **9** | `01:30` | Cửa sổ 3 (`STAFF`) | Mở `/staff/check-in`, chọn đúng suất chiếu vừa tạo trên `#showtimeSelect`. Tại mục **Tải ảnh QR (PNG/JPG)** (`#qrFileInput`), chọn tệp `ticket-A1.png` vừa lưu. | Trình duyệt giải mã ảnh PNG tất định qua `#qrFileScratch` (`decodeQrFileDeterministically`), gọi API `/api/staff/tickets/scan` và hiển thị huy hiệu xanh **`HỢP LỆ`** (`ACCEPTED`); trạng thái vé chuyển từ `ISSUED` sang `USED`. |
| **10** | `00:45` | Cửa sổ 3 (`STAFF`) | Nhấn nút **Quét lại vé này** (hoặc tải lại chính tệp `ticket-A1.png` lần thứ hai liên tiếp). | Hệ thống từ chối ngay lập tức với cảnh báo vàng **`ĐÃ SỬ DỤNG`** (`ALREADY_USED`) kèm thời điểm đã quét ở Bước 9 (`BR-13`). |
| **11** | `01:00` | Cửa sổ 3 (`STAFF`) | Đổi sang chọn một suất chiếu khác trên `#showtimeSelect` rồi tải lại tệp `ticket-A1.png` $\rightarrow$ báo **`SAI SUẤT CHIẾU`** (`WRONG_SHOWTIME`). Nhập thử mã giả `FAKE_QR_123` $\rightarrow$ báo **`KHÔNG TÌM THẤY VÉ`** (`NOT_FOUND`). Mở `/staff/check-in/history`. | Trang lịch sử soát vé ghi nhận đầy đủ cả lượt quét hợp lệ lẫn các lượt bị từ chối kèm nguyên nhân và nhân viên thực hiện (`FR-AUD-02`). |
| **12** | `01:00` | Cửa sổ 1 (`MANAGER`) | Mở `/management/reports`, lọc theo ngày hôm nay và phim vừa chiếu. | Báo cáo từ `dbo.vw_ShowtimeSales` hiển thị chính xác: **1 đơn `PAID`**, **2 vé đã bán**, doanh thu **`170.000 đ`**, tỷ lệ lấp đầy khớp tuyệt đối với sức chứa phòng. Việc quét vé ở Bước 9–11 không làm thay đổi doanh thu. |
| **13** | `01:00` | PowerShell | Chạy lệnh `dotnet run --project .\src\BetaGiaiPhong.Runner -- verify` trên PowerShell. | Chứng minh **9/9 nhóm bất biến SQL Server đạt `0` vi phạm (`INTEGRITY PASSED`)**, **166/166 xUnit tests PASS** và **5/5 Playwright Chromium E2E suites PASS**. |
| **14** | Thời gian còn lại | Toàn hệ thống | Trả lời câu hỏi phản biện của Hội đồng theo Bảng Gợi ý Trả lời Nhanh ở Mục 4. | — |

---

## 3. Các Phương án Dự phòng Tình huống Khẩn cấp (Contingency Plans)

| Tình huống phát sinh tại phòng bảo vệ | Phương án xử lý tức thời (Zero-Downtime Fallback) |
|---|---|
| **1. Không có webcam/camera điện thoại hoặc ánh sáng máy chiếu làm lóa camera** | Sử dụng tính năng **Tải ảnh QR (PNG/JPG) (`#qrFileInput`)** ngay trên trình duyệt máy tính tại `/staff/check-in` (như Bước 9). Tính năng này giải mã trực tiếp ma trận điểm ảnh PNG trên trình duyệt và gọi cùng một API `/api/staff/tickets/scan`. |
| **2. Phòng bảo vệ mất kết nối Internet / Wi-Fi chập chờn** | Toàn bộ hệ thống (`BetaGiaiPhong.Web`, `SQL Server 2022`, thư viện `Bootstrap`, `jQuery`, `html5-qrcode`) đều được đóng gói cục bộ (`wwwroot/lib/`) và chạy 100% offline trên `http://localhost:5145` mà không phụ thuộc CDN bên ngoài. |
| **3. Hội đồng yêu cầu rút ngắn phần demo xuống dưới 5 phút (Chuyển sang Chế độ 2)** | Chạy `dotnet run --project .\src\BetaGiaiPhong.Runner -- seed-checkin-demo` để có ngay suất chiếu ở `now + 15 phút` và vé `PAID` sẵn sàng; thực hiện thẳng: **Chọn ghế & Thanh toán (1.5 phút) $\rightarrow$ Quét ảnh QR lần 1 `HỢP LỆ` & lần 2 `ĐÃ SỬ DỤNG` (1.5 phút) $\rightarrow$ Xem Báo cáo Doanh thu & Chạy `Runner verify` (1.5 phút)**. |
| **4. Quên đặt giờ suất chiếu trong khoảng `-30m..+15m` khiến lúc quét báo `NGOÀI KHUNG GIỜ`** | Giải thích với Hội đồng đây chính là minh chứng quy tắc `BR-14` / `CR-08` đang bảo vệ cổng soát vé; sau đó chạy `dotnet run --project .\src\BetaGiaiPhong.Runner -- seed-checkin-demo` để có ngay suất chiếu nằm ở `now + 15 phút` và quét `HỢP LỆ`. |
| **5. Khởi động lại máy chủ Web và cần giữ nguyên các vé QR đã phát hành** | Luôn gọi `. .\scripts\01_prepare_dev_session.ps1` trước khi chạy lại `dotnet run --project .\src\BetaGiaiPhong.Web --launch-profile http` để đồng bộ đúng khóa `BGP_QR_AES_KEY_B64` từ `.NET User Secrets`. |

---

## 4. Bảng Hỏi – Đáp Nhanh Phục vụ Phản biện Trước Hội đồng (Q&A Cheat-Sheet)

1. **Hỏi: Làm sao hệ thống đảm bảo khi 20–30 người cùng bấm đặt ghế `A1` cùng lúc thì không có 2 người cùng giữ được ghế và không bị Deadlock?**
   - **Đáp:** Trong `SeatReservationService.ReserveAsync`, hệ thống mở giao dịch `IsolationLevel.Serializable` và phát lệnh khóa dòng suất chiếu `SELECT ... FROM dbo.Showtimes WITH (UPDLOCK, HOLDLOCK) WHERE showtime_id = @p0` ngay đầu giao dịch. Khóa cập nhật `UPDLOCK` buộc các giao dịch cùng suất chiếu xếp hàng tuần tự tại SQL Server trước khi đọc trạng thái ghế, giúp đúng 1 yêu cầu thành công và các yêu cầu còn lại nhận `409 Conflict` mà không xảy ra Deadlock nâng cấp khóa.
2. **Hỏi: Nếu khách hàng giữ ghế 5 phút, đến phút thứ 6 đơn đã chuyển sang `EXPIRED` và ghế đã bán cho người khác, nhưng lúc đó cổng thanh toán mới gửi callback báo trừ tiền thành công cho đơn đầu tiên thì xử lý thế nào?**
   - **Đáp:** Theo quyết định kiến trúc `CR-01` trong `PaymentService.ApplyVerifiedMockAsync`, trạng thái `EXPIRED` của đơn hàng là trạng thái cuối bất biến. Hệ thống giữ nguyên `Orders.status = 'EXPIRED'`, chuyển giao dịch thanh toán sang `Payments.status = 'NEEDS_REVIEW'`, ghi nhận sự kiện vào `PaymentEvents` và `AuditLogs` để bộ phận kế toán đối soát hoàn tiền, tuyệt đối không cấp vé và không ghi đè ghế của khách hàng mới.
3. **Hỏi: Tại sao trong bảng `Tickets` lại có cả 2 cột `qr_token_hash` và `qr_token_cipher` mà không lưu thẳng chuỗi `qr_token`?**
   - **Đáp:** Nếu lưu văn bản rõ (`plaintext`), bất kỳ ai có quyền đọc DB hoặc bản sao lưu đều có thể lấy cắp mã vé của khách hàng. Do đó hệ thống dùng `qr_token_hash` (`SHA-256`, `binary(32)`, có Unique Index) để tra cứu nhanh $O(1)$ khi nhân viên quét vé tại cổng, và dùng `qr_token_cipher` (`AES-256-GCM`) với khóa 256-bit nằm ngoài DB để giải mã tạm thời trong bộ nhớ RAM khi vẽ ảnh PNG mã QR cho đúng chủ đơn hàng.
4. **Hỏi: Làm sao ngăn chặn việc gán nhầm ghế của Phòng 02 vào một suất chiếu diễn ra ở Phòng 01?**
   - **Đáp:** Ngoài việc kiểm tra ở tầng ứng dụng, cơ sở dữ liệu sử dụng khóa ngoại kép (`CR-02`): bảng `ShowtimeSeats` có khóa ngoại `(showtime_id, room_id)` trỏ về `Showtimes` và `(room_id, seat_id)` trỏ về `Seats`. Động cơ SQL Server sẽ từ chối mọi bản ghi nếu `room_id` của ghế và `room_id` của suất chiếu không trùng nhau (`TC_INT_01`).
5. **Hỏi: Nếu sau khi đã bán 10 vé ở mức giá `75.000 đ`, người quản lý vô hiệu hóa bớt ghế bảo trì thì báo cáo doanh thu và tỷ lệ lấp đầy của suất chiếu cũ có bị sai lệch không?**
   - **Đáp:** Không bị sai lệch nhờ hai cơ chế đóng băng lịch sử (`CR-05` và `CR-07`): đơn giá từng ghế tại thời điểm mua được chụp cố định vào `OrderSeats.unit_price_vnd`, còn tổng số ghế có thể bán của phòng tại thời điểm mở bán suất chiếu được đóng băng vào `Showtimes.sellable_capacity_snapshot`.
6. **Hỏi: Khi một nhân viên soát vé bị phát hiện vi phạm và bị khóa tài khoản trong CSDL, cookie đăng nhập cũ của nhân viên đó có còn quét vé được nữa không?**
   - **Đáp:** Không. Hệ thống cài đặt sự kiện `CookieAuthenticationEvents.OnValidatePrincipal` trong `Program.cs` kiểm tra trạng thái `Users.status == 'ACTIVE'` và bảng `UserRoles` trên mỗi request. Ngay ở request kế tiếp sau khi bị khóa hoặc thu hồi quyền, phiên đăng nhập bị từ chối và đăng xuất tức thời (`TC_WEB_AUTH_08`, `TC_WEB_AUTH_09`, `03-restart-recovery.spec.ts`).
