# NỘI DUNG KỸ THUẬT PHỤC VỤ BÁO CÁO ĐỀ ÁN TỐT NGHIỆP (`THESIS_TECHNICAL_CONTENT`)

**Tên đề tài:** Phân tích, thiết kế và xây dựng hệ thống quản lý bán vé trực tuyến tại rạp chiếu phim Beta Giải Phóng  
**Sinh viên thực hiện:** Vũ Văn Khiêm — Mã sinh viên: `11235581`  
**Ngành:** Hệ thống thông tin quản lý (Management Information Systems — MIS)  
**Giảng viên hướng dẫn:** ThS. Nguyễn Anh Phương  
**Kho mã nguồn kiểm chứng:** `https://github.com/cakhiempn-a11y/beta-giai-phong-ticketing-system`

---

## CHƯƠNG 1. TỔNG QUAN ĐỀ TÀI, BỐI CẢNH NGHIỆP VỤ VÀ PHẠM VI NGHIÊN CỨU

### 1.1. Bối cảnh nghiệp vụ tại rạp chiếu phim Beta Giải Phóng
Rạp chiếu phim **Beta Giải Phóng** (thuộc phân khúc rạp chiếu phim đại chúng hướng tới học sinh, sinh viên và gia đình tại khu vực phía Nam Hà Nội) có đặc thù lưu lượng khách hàng tập trung rất cao vào các khung giờ vàng buổi tối và dịp cuối tuần. Trong bài toán bán vé rạp chiếu phim trực tuyến, tài nguyên cốt lõi là **từng vị trí ghế cụ thể trong một suất chiếu xác định (`ShowtimeSeat`)** — một loại hàng hóa hữu hạn, không thể thay thế tương đương và có tính thời điểm nghiêm ngặt.

Khác với thương mại điện tử bán lẻ thông thường (nơi tồn kho được trừ theo số lượng tổng), hệ thống bán vé rạp chiếu phim phải giải quyết đồng thời bốn thách thức kỹ thuật đặc thù của ngành Hệ thống Thông tin Quản lý (MIS):
1. **Bài toán tranh chấp tài nguyên đồng thời (Concurrency & Double-booking Prevention):** Khi hàng chục khách hàng cùng nhấn chọn một ghế đẹp (ví dụ ghế trung tâm phòng chiếu) tại cùng một thời điểm, hệ thống phải đảm bảo tính nguyên tử *All-or-Nothing* — chỉ đúng một giao dịch được quyền giữ ghế, các giao dịch còn lại bị từ chối an toàn mà không giữ dở dang một phần danh sách ghế.
2. **Bài toán quản lý vòng đời giữ ghế tạm thời (Time-bounded Seat Hold):** Ghế đang trong quá trình thanh toán (`HELD`) phải được khóa tạm thời trong 5 phút nhưng phải tự động giải phóng về trạng thái trống (`AVAILABLE`) nếu khách hàng bỏ dở phiên thanh toán hoặc quá hạn, tránh hiện tượng "ghế ma" làm thất thoát doanh thu của suất chiếu.
3. **Bài toán xử lý kết quả thanh toán bất đồng bộ và lũy đẳng (Asynchronous & Idempotent Payment Processing):** Cổng thanh toán trực tuyến có thể gửi lặp thông báo webhook nhiều lần do lỗi mạng hoặc gửi thông báo thành công đến muộn sau khi đơn hàng đã hết hạn giữ ghế. Hệ thống không được phép căn cứ vào thao tác chuyển hướng trình duyệt (browser redirect) của khách hàng để cấp vé, không được cấp vé trùng khi nhận callback lặp và không được cấp vé đè lên ghế đã giải phóng khi tiền về muộn.
4. **Bài toán chống giả mạo và sử dụng lại vé điện tử tại quầy (Cryptographic QR Ticket Verification):** Mã QR trên vé điện tử phải không thể đoán trước, không lưu lộ liễu dưới dạng văn bản rõ trong cơ sở dữ liệu và đảm bảo mỗi vé chỉ được phép quét qua cổng kiểm soát (`ISSUED -> USED`) đúng một lần duy nhất.

### 1.2. Phạm vi nguyên mẫu học thuật và tính trung thực dữ liệu
Đề án được thực hiện dưới dạng **nguyên mẫu học thuật độc lập (Academic Capstone Prototype)** với ranh giới phạm vi rõ ràng:
- **Dữ liệu công khai có thật:** Tên thương hiệu và bối cảnh điểm rạp Beta Giải Phóng được tham khảo từ nguồn thông tin công khai để định hình bài toán nghiệp vụ.
- **Dữ liệu và dịch vụ mô phỏng:** Sơ đồ ghế chi tiết trong phòng chiếu, danh mục phim thử nghiệm, lịch chiếu, đơn hàng, cổng thanh toán giả lập ký số `HMAC-SHA256` (`MOCK_GATEWAY`) và số liệu doanh thu trong hệ thống đều là **dữ liệu mô phỏng phục vụ nghiên cứu**, hoàn toàn không kết nối vào hệ thống nội bộ của Beta Cinemas và không phát sinh giao dịch tiền thật.

---

## CHƯƠNG 2. CƠ SỞ LÝ THUYẾT VÀ PHƯƠNG PHÁP THIẾT KẾ KỸ THUẬT

### 2.1. Phương pháp luận phân tích và mô hình hóa hệ thống thông tin
Đề án áp dụng chuỗi phương pháp luận chuẩn quốc tế để đảm bảo tính liên thông từ yêu cầu nghiệp vụ xuống mã nguồn thực thi:
- **Đặc tả yêu cầu phần mềm theo chuẩn ISO/IEC/IEEE 29148:2018:** Định danh tường minh **54 yêu cầu chức năng (`FR-*`)**, **18 quy tắc nghiệp vụ (`BR-*`)**, **18 yêu cầu phi chức năng (`NFR-*`)**, **9 quyết định kiến trúc bổ sung (`CR-01`..`CR-09`)** và **25 ca kiểm thử nghiệm thu (`TC-01`..`TC-25`)**.
- **Mô hình hóa quy trình nghiệp vụ bằng BPMN 2.0.2:** Thiết kế 6 sơ đồ quy trình TO-BE (`BPMN-01` Đặt vé & giữ ghế, `BPMN-02A` Xử lý webhook thanh toán & đối soát, `BPMN-02B` Hủy đơn & thu hồi ghế quá hạn, `BPMN-03` Công bố suất chiếu, `BPMN-04` Soát vé QR, `BPMN-05` Báo cáo doanh thu).
- **Mô hình hóa hướng đối tượng bằng UML 2.5.1:** Xây dựng 1 sơ đồ Use Case tổng quát (`16 Use Cases` cho 5 tác nhân `GUEST`, `CUSTOMER`, `STAFF`, `MANAGER`, `ADMIN`) và **14 sơ đồ tuần tự (`SD-01`..`SD-14`)** mô tả chi tiết luồng thông điệp cùng khối giao dịch `BEGIN / COMMIT / ROLLBACK`.

### 2.2. Lý thuyết kiểm soát đồng thời trong Hệ quản trị CSDL SQL Server
Để ngăn chặn hiện tượng *Lost Update* và *Phantom Read* khi kiểm tra chồng lấn lịch chiếu hoặc khi nhiều khách hàng cùng giữ một tập ghế, hệ thống không dựa vào kiểm tra ở tầng bộ nhớ ứng dụng mà sử dụng trực tiếp cơ chế khóa của SQL Server:
- **Mức cô lập giao dịch `IsolationLevel.Serializable` kết hợp gợi ý khóa `WITH (UPDLOCK, HOLDLOCK)`:**
  - Khi hai giao dịch cùng đọc trạng thái của một suất chiếu hoặc một dòng ghế để chuẩn bị cập nhật, nếu chỉ dùng `READ COMMITTED` hoặc `REPEATABLE READ` với khóa chia sẻ (`S-lock`), cả hai cùng đọc được trạng thái `AVAILABLE` rồi mới yêu cầu nâng cấp lên khóa độc quyền (`X-lock`), dẫn đến Deadlock (SQL Error 1205).
  - Việc phát lệnh `SELECT ... WITH (UPDLOCK, HOLDLOCK)` trên dòng `Showtimes` ngay đầu giao dịch buộc SQL Server cấp khóa cập nhật (`U-lock`) giữ đến hết giao dịch. Vì `U-lock` không tương thích với một `U-lock` khác, các yêu cầu đặt ghế vào cùng một suất chiếu được xếp hàng tuần tự một cách trật tự, triệt tiêu cả nguy cơ bán trùng ghế lẫn hiện tượng Deadlock chéo khi các khách hàng chọn danh sách ghế theo thứ tự khác nhau.
- **Phân cấp thứ tự khóa (Locking Hierarchy — `CR-07`):** Khi công bố suất chiếu mới, dịch vụ `ShowtimePublishingService` luôn khóa dòng `Movies` trước rồi mới khóa dòng `Rooms`, ngăn ngừa chu trình chờ đợi vòng tròn giữa hai luồng quản trị.

### 2.3. Cơ sở mật mã học ứng dụng trong bảo vệ giao dịch và vé điện tử
1. **Bảo vệ mật khẩu người dùng:** Sử dụng hàm dẫn xuất khóa `PBKDF2-HMAC-SHA256` với `100.000` vòng lặp và `128-bit` salt ngẫu nhiên cho từng tài khoản.
2. **Xác thực nguồn gốc Webhook thanh toán:** Sử dụng mã xác thực thông điệp `HMAC-SHA256` trên chuỗi dữ liệu chuẩn hóa và so sánh bằng hàm thời gian hằng `CryptographicOperations.FixedTimeEquals`.
3. **Bảo vệ vé điện tử QR hai lớp (`SHA-256` + `AES-256-GCM` — `CR-06`):**
   - Mỗi vé được sinh một chuỗi ngẫu nhiên mật mã `256-bit` (`32 byte` từ `RandomNumberGenerator`), biểu diễn dưới dạng Base64URL dài 43 ký tự.
   - Cơ sở dữ liệu **không lưu chuỗi gốc** mà chỉ lưu:
     - `qr_token_hash` (`binary(32)`): Giá trị băm một chiều `SHA-256` có đánh chỉ mục duy nhất (`UQ_Tickets_04`) để tra cứu tốc độ cao khi nhân viên quét vé mà không cần giải mã toàn bộ bảng.
     - `qr_token_cipher` (`varbinary(max)`): Bản mã xác thực `AES-256-GCM` (gồm `12-byte Nonce || 16-byte Tag || Ciphertext`) được mã hóa bằng khóa 256-bit nằm ngoài CSDL, chỉ được giải mã tạm thời trong bộ nhớ RAM khi kết xuất ảnh PNG mã QR cho đúng chủ sở hữu đơn hàng.

---

## CHƯƠNG 3. PHÂN TÍCH VÀ THIẾT KẾ HỆ THỐNG QUẢN LÝ BÁN VÉ TRỰC TUYẾN

### 3.1. Thiết kế kiến trúc phần mềm (Modular Monolith)
Hệ thống được tổ chức thành 4 dự án .NET 10 và 1 dự án kiểm thử trình duyệt Playwright:
1. **`BetaGiaiPhong.Data`**: Tầng miền nghiệp vụ và lưu trữ dữ liệu (17 thực thể EF Core 10, 17 lớp cấu hình Fluent API, 2 migrations, 5 dịch vụ giao dịch lõi và các mô-đun khóa/mật mã).
2. **`BetaGiaiPhong.Web`**: Tầng trình diễn MVC Razor Views, các RESTful API Controllers, dịch vụ truy vấn hiển thị, xác thực Cookie RBAC (`OnValidatePrincipal`) và tiến trình nền `ReservationExpiryWorker`.
3. **`BetaGiaiPhong.Runner`**: Công cụ CLI kiểm định độc lập thực thi 9 nhóm truy vấn kiểm toán bất biến dữ liệu trực tiếp trên SQL Server.
4. **`BetaGiaiPhong.Tests` & `e2e/`**: Tầng đảm bảo chất lượng tự động (166 bài test xUnit và 5 bộ test trình duyệt thực Playwright Chromium).

### 3.2. Thiết kế cơ sở dữ liệu mức vật lý (17 Bảng, 116 Cột, 22 Khóa ngoại)
Mô hình dữ liệu quan hệ được chuẩn hóa đạt dạng chuẩn 3 (3NF) kèm các cột chụp lịch sử có chủ đích (`seat_type_snapshot`, `unit_price_vnd`, `sellable_capacity_snapshot`) để bảo toàn dữ liệu kế toán khi cấu hình danh mục thay đổi theo thời gian:
- **Khóa ngoại kép chống sai lệch chéo phòng và chéo suất chiếu (`CR-02`, `CR-04`):**
  - Bảng `ShowtimeSeats` tham chiếu đồng thời tới `Showtimes(showtime_id, room_id)` và `Seats(seat_id, room_id)`, đảm bảo ở mức động cơ SQL Server rằng không bao giờ có thể gán một ghế của Phòng 02 vào suất chiếu diễn ra tại Phòng 01.
  - Bảng `OrderSeats` tham chiếu tới `Orders(order_id, showtime_id)` và `ShowtimeSeats(showtime_id, seat_id)`, còn bảng `Tickets` tham chiếu tới `OrderSeats(order_id, showtime_id, seat_id)`, tạo thành chuỗi ràng buộc tham chiếu khép kín từ Phòng $\rightarrow$ Suất chiếu $\rightarrow$ Ghế suất chiếu $\rightarrow$ Đơn hàng $\rightarrow$ Chi tiết ghế đặt $\rightarrow$ Vé điện tử.
- **Chính sách bảo toàn lịch sử giao dịch (`CR-09`):** Toàn bộ **22/22 khóa ngoại** trong CSDL đều được thiết lập `ON DELETE NO ACTION` (`DeleteBehavior.NoAction`), ngăn chặn mọi lệnh xóa dây chuyền (`CASCADE DELETE`) có thể làm mất dấu vết đơn hàng, thanh toán hoặc nhật ký kiểm toán.
- **View thống kê doanh thu (`dbo.vw_ShowtimeSales`):** Đóng gói công thức tính số ghế bán (`SOLD`), số ghế đang giữ (`HELD`), doanh thu thực nhận (chỉ tính đơn `PAID` theo `BR-17`) và tỷ lệ lấp đầy ($\text{sold\_seats\_count} \times 100.0 / \text{sellable\_capacity\_snapshot}$ theo `BR-18` và `CR-07`).

---

## CHƯƠNG 4. HIỆN THỰC HÓA NGUYÊN MẪU VÀ KẾT QUẢ KIỂM THỬ THỰC NGHIỆM

### 4.1. Hiện thực hóa các luồng giao dịch trọng yếu
1. **Luồng công bố suất chiếu (`ShowtimePublishingService.PublishAsync`):**
   - Kiểm tra quyền `MANAGER`/`ADMIN`, khóa tuần tự `Movie -> Room`, kiểm tra trạng thái phim `PUBLISHED` và phòng `ACTIVE`.
   - Kiểm tra công thức chồng lấn thời gian trên mọi suất chiếu chưa hủy của cùng phòng:
     $$\text{ExistingStartUtc} < \text{NewEndUtc} + \text{NewBuffer} \quad \land \quad \text{NewStartUtc} < \text{ExistingEndUtc} + \text{ExistingBuffer}$$
   - Đóng băng `sellable_capacity_snapshot`, lưu bảng giá `SeatPrices`, sinh nguyên tử toàn bộ dòng `ShowtimeSeats (AVAILABLE)` cho các ghế `is_active = 1` và ghi `AuditLogs`.
2. **Luồng giữ ghế nguyên tử (`SeatReservationService.ReserveAsync`):**
   - Khóa `Showtime` bằng `UPDLOCK, HOLDLOCK`, thu hồi tức thời các đơn `PENDING_PAYMENT` đã quá hạn trên suất chiếu đó trước khi xét ghế.
   - Kiểm tra số lượng ghế $1 \le N \le 8$, trạng thái phim `PUBLISHED`, trạng thái suất chiếu `ON_SALE`.
   - Nếu toàn bộ $N$ ghế đều `AVAILABLE`, tạo đơn `PENDING_PAYMENT` với hạn giữ 5 phút (`hold_expires_utc`), chụp đơn giá vào `OrderSeats` và chuyển toàn bộ $N$ ghế sang `HELD`. Nếu bất kỳ ghế nào không còn trống, hủy bỏ toàn bộ giao dịch và trả mã `409 Conflict`.
3. **Luồng xử lý Webhook thanh toán và đối soát muộn (`PaymentService.ApplyVerifiedMockAsync`):**
   - Xác thực chữ ký `HMAC-SHA256`; kiểm tra tính lũy đẳng qua `(provider, provider_event_ref)`.
   - Nếu đơn đang `PENDING_PAYMENT` và chưa quá hạn: chuyển `Payments -> SUCCESS`, `Orders -> PAID`, `ShowtimeSeats -> SOLD`, phát hành vé điện tử QR (`Tickets`) cho từng ghế.
   - Nếu thông báo `SUCCESS` đến muộn sau khi đơn đã `EXPIRED` hoặc `CANCELLED` (`CR-01`): giữ nguyên trạng thái cuối của `Orders`, đánh dấu `Payments.status = 'NEEDS_REVIEW'` và ghi nhật ký đối soát, tuyệt đối không cấp vé và không ghi đè ghế của khách khác.
4. **Luồng soát vé QR tại cổng (`TicketScanningService.ScanAsync`):**
   - Tính băm `SHA-256` của chuỗi token quét được, khóa trực tiếp dòng `Tickets` bằng `UPDLOCK, HOLDLOCK`.
   - Kiểm tra khớp suất chiếu (`WRONG_SHOWTIME`), khung giờ soát vé từ `start_utc - 30 phút` đến `end_utc + 15 phút` (`OUTSIDE_WINDOW`), trạng thái đơn `PAID` và trạng thái vé `ISSUED` (`ACCEPTED` chuyển sang `USED`, hoặc từ chối với `ALREADY_USED`), đồng thời ghi nhận đầy đủ vào `TicketScans` và `AuditLogs`.

### 4.2. Kết quả kiểm thử tự động và đo đạc thực nghiệm
Hệ thống đã vượt qua toàn bộ các cổng chất lượng tự động trên cả máy trạm Windows và GitHub Actions Linux CI:
- **Kiểm thử xUnit (.NET 10):** **166 / 166 bài kiểm thử PASS** (**10** Unit Tests, **35** SQL Server Integration & Concurrency Tests trên DB cô lập theo GUID với cơ chế bảo vệ quyền sở hữu `BGP_TestRunOwnershipToken`, và **121** `WebApplicationFactory` Functional/Security Tests).
- **Kiểm thử trình duyệt thực Playwright Chromium E2E:** **5 / 5 bộ spec PASS** (`01-full-journey`, `02-negative-and-security`, `03-restart-recovery`, `04-responsive-and-a11y`, `05-performance-load`), chứng minh trọn vẹn chu trình **Manager mở bán $\rightarrow$ Customer đặt ghế & thanh toán $\rightarrow$ Staff tải ảnh QR PNG soát vé $\rightarrow$ Manager đối soát báo cáo doanh thu**.
- **Kiểm định bất biến SQL Server (`Runner verify`):** **9 / 9 nhóm bất biến đạt 0 vi phạm** sau mọi kịch bản tranh chấp và khởi động lại.
- **Đo đạc hiệu năng và tải đồng thời (`05-performance-load.spec.ts`):**
  - Độ trễ `p95` của các thao tác tra cứu danh mục và sơ đồ ghế đạt **`12,74 ms – 18,04 ms`** (thấp hơn nhiều so với ngưỡng yêu cầu $\le 1.000\text{ ms}$ của `NFR-01`).
  - Độ trễ `p95` của thao tác giữ ghế, thanh toán cấp vé và quét vé QR đạt **`16,06 ms – 492,36 ms`** (đạt yêu cầu $\le 1.500\text{ ms}$ của `NFR-02`).
  - Trong bài toán **30 luồng gửi yêu cầu đặt cùng 1 ghế đồng thời**, hệ thống xử lý với độ trễ `p95 = 275,88 ms`, thông lượng `99,76 req/s`: đúng **1 yêu cầu thành công (`201 Created`)**, **29 yêu cầu nhận phản hồi xung đột hợp lệ (`409 Conflict`)** và **`0` lỗi máy chủ (`5xx`)**.

---

## CHƯƠNG 5. ĐÁNH GIÁ KẾT QUẢ, GIỚI HẠN CỦA NGUYÊN MẪU VÀ HƯỚNG PHÁT TRIỂN

### 5.1. Đánh giá mức độ hoàn thành mục tiêu đề án
Đề án đã hoàn thành mục tiêu phân tích, thiết kế và xây dựng một hệ thống quản lý bán vé trực tuyến có độ toàn vẹn giao dịch cao cho rạp Beta Giải Phóng:
- Bao phủ **54 yêu cầu chức năng (`52 P0 + 2 P1`)**, trong đó đối với **52 yêu cầu P0** có **46 yêu cầu `IMPLEMENTED`** toàn diện, **5 yêu cầu `PARTIALLY IMPLEMENTED`** ở tầng CSDL/Service/Read-UI và **1 yêu cầu `NOT IMPLEMENTED`** (`FR-AUTH-02`); đối với **2 yêu cầu P1** có **1 `PARTIALLY IMPLEMENTED`** (`FR-AUTH-06`) và **1 `OUT OF SCOPE`** (`FR-RPT-05`). Đạt **18/18 quy tắc nghiệp vụ (`BR`)**, **18/18 yêu cầu phi chức năng (`NFR`)**, **9/9 quyết định kiến trúc (`CR`)** và tự động hóa **24/25 ca kiểm thử nghiệm thu (`TC`)**.
- Đảm bảo tính nhất quán tuyệt đối giữa sơ đồ quy trình BPMN, sơ đồ tuần tự UML, mô hình dữ liệu vật lý 17 bảng trên SQL Server và mã nguồn C# thực thi.

### 5.2. Các giới hạn được ghi nhận trung thực của nguyên mẫu (Honest Limitations)
1. **Chức năng tự đăng ký tài khoản khách hàng (`FR-AUTH-02` / `UC-02`):** Nguyên mẫu sử dụng bộ tài khoản khách hàng và nhân sự được khởi tạo sẵn có kiểm soát qua `DemoSeeder` để phục vụ kiểm thử giao dịch; chưa xây dựng biểu mẫu tự đăng ký công khai `/account/register`.
2. **Các giao diện quản trị phụ trợ và tính năng ưu tiên P1 (`FR-AUTH-06`, `FR-RPT-05`, `FR-AUD-02`):** Cơ chế thu hồi quyền tức thời ở tầng middleware (`OnValidatePrincipal`) và ghi nhật ký kiểm toán (`AuditLogs`) đã hoạt động đầy đủ trong CSDL, nhưng chưa tách thành màn hình quản lý nhân sự `/admin/users` hay tính năng xuất tệp CSV (`P1`).
3. **Nghiệm thu phần cứng Camera vật lý (`MANUAL UAT — NOT TESTED`):** Quá trình tạo và giải mã ảnh PNG mã QR trên trình duyệt Chromium (`Html5Qrcode.scanFile`) đã được tự động hóa 100%, nhưng việc quét qua ống kính quang học của điện thoại di động thực tế ngoài đời thực cần được kiểm tra thủ công theo quy trình `MANUAL_CAMERA_UAT.md`.
4. **Phạm vi đo hiệu năng:** Số liệu độ trễ và thông lượng được đo trên môi trường nguyên mẫu một máy chủ cục bộ (single-node empirical baseline), không đại diện cho cam kết SLA trên hạ tầng đám mây phân tán.

### 5.3. Hướng phát triển sau phiên bản 1.0
1. Tích hợp cổng thanh toán thương mại thực tế (VNPay, MoMo, VietQR) thông qua adapter kết nối vào `PaymentService` hiện hữu và xây dựng quy trình hoàn tiền có phê duyệt cho các giao dịch `NEEDS_REVIEW`.
2. Bổ sung phân hệ bán kèm combo bắp nước (Concession items), mã khuyến mại đa điều kiện (Voucher/Promotion engine) và tích điểm thành viên.
3. Mở rộng lưu trữ khóa Data Protection và hàng đợi thông báo vé qua Email/Zalo ZNS trên kiến trúc đám mây đa thực thể (Multi-instance Cloud Deployment).
