# HƯỚNG DẪN SỬ DỤNG HỆ THỐNG THEO VAI TRÒ (`USER_MANUAL`)

**Đề tài:** Phân tích, thiết kế và xây dựng hệ thống quản lý bán vé trực tuyến tại rạp chiếu phim Beta Giải Phóng  
**Tác giả:** Vũ Văn Khiêm (`11235581`) — Ngành Hệ thống thông tin quản lý (MIS)  
**Giảng viên hướng dẫn:** ThS. Nguyễn Anh Phương

---

## 1. Tổng quan các Vai trò Người dùng trong Hệ thống

Hệ thống phân tách quyền hạn nghiêm ngặt theo 5 vai trò nghiệp vụ:

| Vai trò | Đường dẫn trang chính | Các chức năng được phép sử dụng |
|---|---|---|
| **Khách vãng lai (`GUEST`)** | `/`, `/movies`, `/movies/{id}`, `/showtimes/{id}/seats` | Tra cứu danh mục phim đang chiếu (`PUBLISHED`), lọc phim theo từ khóa/thể loại/độ tuổi/ngày chiếu, xem lịch chiếu (`ON_SALE`) và xem sơ đồ ghế cùng bảng giá vé. |
| **Khách hàng (`CUSTOMER`)** | Kế thừa `GUEST` + `/orders`, `/orders/{id}`, `/orders/{id}/checkout`, `/orders/{id}/tickets` | Chọn từ 1–8 ghế, giữ ghế nguyên tử trong 5 phút, thực hiện thanh toán mô phỏng, chủ động hủy đơn chờ thanh toán, xem lịch sử đơn hàng và tải vé điện tử QR PNG. |
| **Nhân viên soát vé (`STAFF`)**| `/staff/check-in`, `/staff/check-in/history` | Chọn suất chiếu đang mở cổng, xác thực vé điện tử QR bằng 3 phương thức (Camera trực tiếp, Tải ảnh QR PNG, Nhập mã token) và tra cứu lịch sử soát vé. |
| **Quản lý rạp (`MANAGER`)** | `/management`, `/management/movies`, `/management/rooms`, `/management/showtimes`, `/management/reports` | Quản lý vòng đời phim (`DRAFT -> PUBLISHED -> ARCHIVED`), xem sơ đồ phòng chiếu, tạo & mở bán suất chiếu mới (chống trùng lịch + cấu hình giá ghế), đóng/hủy suất chiếu và xem Báo cáo doanh thu / Tỷ lệ lấp đầy. |
| **Quản trị viên (`ADMIN`)** | Toàn bộ các trang trên | Kế thừa toàn bộ quyền của `MANAGER` và `STAFF`. |

---

## 2. Hướng dẫn Sử dụng cho Khách vãng lai (`GUEST`) & Khách hàng (`CUSTOMER`)

### 2.1. Tra cứu Phim và Lịch chiếu (`/movies`)
1. Truy cập trang chủ `/` hoặc `/movies`.
2. Sử dụng thanh công cụ lọc để tìm kiếm theo:
   - **Từ khóa tên phim** (ví dụ: `Đào, Phở và Piano`, `Mai`, ...).
   - **Nhãn độ tuổi** (`P` — Phổ biến mọi độ tuổi, `K` — Dưới 13 tuổi có người giám hộ, `T13`, `T16`, `T18`).
   - **Ngày chiếu** (tính theo múi giờ Việt Nam `UTC+7`).
3. Nhấn **Xem chi tiết & Đặt vé** trên thẻ phim để mở trang `/movies/{id}` và chọn khung giờ chiếu (`ON_SALE`) mong muốn.

### 2.2. Xem Sơ đồ Ghế và Giữ Ghế Tạm thời (`/showtimes/{id}/seats`)
1. Trên màn hình sơ đồ ghế, hệ thống hiển thị trực quan:
   - **Màu sắc loại ghế & Giá vé:** Ghế Thường (`STANDARD`), Ghế VIP (`VIP`), Ghế Đôi (`COUPLE`).
   - **Trạng thái ghế thời gian thực:**
     - `Trống (AVAILABLE)`: Có thể click chọn.
     - `Đang chọn`: Ghế bạn vừa click chọn (tối đa **8 ghế** cho một đơn hàng).
     - `Đang giữ (HELD)`: Đang được giữ tạm thời trong 5 phút bởi một đơn hàng chờ thanh toán.
     - `Đã bán (SOLD)`: Đã thanh toán thành công, không thể chọn.
     - `Bảo trì (DISABLED)`: Ghế ngừng phục vụ.
2. Khi chọn từ **1 đến 8 ghế**, bảng tóm tắt bên phải tự động cộng tổng tiền tạm tính và bật nút **Tiếp tục đặt vé** (`#btnSubmit`).
3. Nếu chưa đăng nhập, hệ thống tự động chuyển hướng tới `/account/login` và đưa bạn quay lại đúng sơ đồ ghế sau khi đăng nhập thành công.
4. Khi nhấn **Tiếp tục đặt vé**:
   - Nếu toàn bộ ghế bạn chọn đều còn trống, hệ thống khóa ghế nguyên tử (`HELD`) trong **5 phút**, tạo đơn hàng `PENDING_PAYMENT` và chuyển tới trang chi tiết đơn `/orders/{orderId}`.
   - Nếu bất kỳ ghế nào vừa bị người khác đặt trước trong tích tắc, hệ thống báo lỗi xung đột (`409 Conflict`), nêu rõ mã ghế bị trùng và giữ nguyên các ghế còn lại để bạn chọn lại.

### 2.3. Thanh toán Mô phỏng hoặc Hủy Đơn chờ (`/orders/{id}` & `/orders/{id}/checkout`)
1. Tại trang `/orders/{id}`, đồng hồ đếm ngược `#countdownTimer` hiển thị chính xác thời gian giữ ghế còn lại (tính theo giờ máy chủ UTC).
2. **Nếu muốn hủy đơn khi chưa thanh toán:** Nhấn nút **Hủy đơn hàng** $\rightarrow$ đơn chuyển sang `CANCELLED` và các ghế `HELD` lập tức được giải phóng về `AVAILABLE`.
3. **Để thanh toán:** Nhấn **Tiến hành thanh toán** để mở trang `/orders/{id}/checkout`:
   - Trong môi trường nguyên mẫu học thuật (`BGP_ENABLE_MOCK_PAYMENT_SIMULATOR=true`), bạn có thể chọn:
     - **Thanh toán Thành công (`#btnPaySuccess`):** Hệ thống phát sinh giao dịch `Payments`, gửi callback có chữ ký `HMAC-SHA256` hợp lệ, chuyển đơn sang `PAID`, chuyển toàn bộ ghế sang `SOLD` và cấp vé điện tử QR.
     - **Mô phỏng Thanh toán Thất bại (`#btnPayFailed`):** Đơn chuyển sang `FAILED` và giải phóng toàn bộ ghế đang giữ.

### 2.4. Xem và Tải Vé Điện tử QR (`/orders/{id}/tickets`)
1. Ngay sau khi đơn hàng chuyển sang `PAID`, nhấn **Xem vé điện tử (QR)** (hoặc vào mục **Đơn hàng của tôi** `/orders` $\rightarrow$ chọn đơn `PAID`).
2. Mỗi ghế trong đơn được cấp **1 thẻ vé điện tử độc lập** bao gồm:
   - Mã vé (`TicketCode`), Tên phim, Phòng chiếu, Giờ chiếu (giờ VN), Mã ghế, Loại ghế.
   - Trạng thái vé: `HỢP LỆ / CHƯA SOÁT (ISSUED)` hoặc `ĐÃ SOÁT VÉ (USED)`.
   - Hình ảnh **Mã QR (`246x246` px PNG)** được giải mã trực tiếp trong bộ nhớ từ `qr_token_cipher`.
3. Bạn có thể lưu ảnh QR PNG về thiết bị hoặc đưa trực tiếp màn hình cho nhân viên soát vé tại rạp.

---

## 3. Hướng dẫn Sử dụng cho Nhân viên Soát vé (`STAFF`)

### 3.1. Quét và Xác thực Vé Điện tử QR (`/staff/check-in`)
1. Đăng nhập bằng tài khoản `staff@demo.invalid` (hoặc `manager`/`admin`) và mở mục **Soát vé QR** (`/staff/check-in`).
2. **Bước 1 — Chọn suất chiếu đang kiểm soát:** Chọn đúng suất chiếu tại danh sách thả xuống `#showtimeSelect`. *(Lưu ý: Cổng soát vé chỉ mở trong khoảng thời gian từ **30 phút trước giờ bắt đầu** đến **15 phút sau giờ kết thúc** của suất chiếu)*.
3. **Bước 2 — Thực hiện quét mã QR bằng 1 trong 3 cách:**
   - **Cách A (Tải ảnh QR PNG — ổn định nhất khi demo trên máy tính):** Nhấn vào ô **Tải ảnh mã QR từ thiết bị** (`#qrFileInput`) và chọn tệp ảnh `.png` mã QR của khách hàng. Trình duyệt tự động giải mã ảnh QR và gửi xác thực ngay lập tức.
   - **Cách B (Quét bằng Camera trực tiếp):** Nhấn **Bật Camera** (`#btnStartCamera`), cấp quyền truy cập camera và hướng ống kính vào mã QR trên điện thoại khách hàng.
   - **Cách C (Nhập mã hoặc dùng súng quét 2D USB):** Đặt con trỏ vào ô `#manualQrToken`, quét bằng đầu đọc USB hoặc dán chuỗi token và nhấn **Kiểm tra vé**.
4. **Bước 3 — Đọc kết quả trả về trên màn hình:**
   - 🟢 **`HỢP LỆ (ACCEPTED)`:** Vé hợp lệ lần đầu $\rightarrow$ Cho phép khách vào phòng chiếu; vé tự động chuyển sang trạng thái `USED`.
   - 🟡 **`ĐÃ SỬ DỤNG (ALREADY_USED)`:** Vé này đã được check-in trước đó $\rightarrow$ Từ chối cho vào, đối chiếu thời điểm đã quét trên màn hình.
   - 🔴 **`SAI SUẤT CHIẾU (WRONG_SHOWTIME)`:** Vé hợp lệ nhưng thuộc suất chiếu khác với suất đang chọn tại quầy $\rightarrow$ Hướng dẫn khách kiểm tra lại giờ chiếu/phòng chiếu (vé không bị hủy).
   - 🟠 **`NGOÀI GIỜ SOÁT VÉ (OUTSIDE_WINDOW)`:** Chưa tới giờ mở cổng check-in (sớm hơn 30 phút trước giờ chiếu) hoặc suất chiếu đã kết thúc quá 15 phút.
   - ⚫ **`KHÔNG TÌM THẤY VÉ (NOT_FOUND)`:** Mã QR giả mạo hoặc không tồn tại trong hệ thống.

### 3.2. Xem Lịch sử Soát vé (`/staff/check-in/history`)
- Nhấn **Xem lịch sử soát vé** để kiểm tra toàn bộ các lượt quét thành công (`ACCEPTED`) và bị từ chối (`ALREADY_USED`, `WRONG_SHOWTIME`, `OUTSIDE_WINDOW`, `NOT_FOUND`) theo từng suất chiếu kèm thời gian thực hiện và nhân viên thao tác.

---

## 4. Hướng dẫn Sử dụng cho Quản lý Rạp (`MANAGER` / `ADMIN`)

### 4.1. Quản lý Danh mục Phim (`/management/movies`)
1. **Tạo phim mới (`/management/movies/create`):** Nhập tên phim, thời lượng (`30–300` phút), nhãn độ tuổi (`P`, `K`, `T13`, `T16`, `T18`), thể loại và mô tả. Phim mới tạo mặc định ở trạng thái `DRAFT` (chưa hiển thị cho khách hàng).
2. **Xuất bản phim (`Publish`):** Tại trang chi tiết hoặc danh sách phim, nhấn **Xuất bản (Publish)** để chuyển phim từ `DRAFT` sang `PUBLISHED`. Khi đó phim mới xuất hiện trên trang chủ và mới được phép xếp lịch chiếu.
3. **Lưu trữ phim (`Archive`):** Chỉ cho phép chuyển phim sang `ARCHIVED` khi phim không còn suất chiếu tương lai đang mở bán.

### 4.2. Xem Phòng chiếu và Ma trận Ghế (`/management/rooms`)
- Truy cập `/management/rooms` và nhấn vào từng phòng chiếu để xem cấu hình sơ đồ ghế vật lý (số hàng, số cột, phân bổ ghế `STANDARD`, `VIP`, `COUPLE` và tổng sức chứa ghế đang hoạt động).

### 4.3. Công bố Suất chiếu Mới và Cấu hình Giá vé (`/management/showtimes/create`)
1. Chọn **Phim** (đang ở trạng thái `PUBLISHED`) và **Phòng chiếu** (`ACTIVE`).
2. Chọn **Thời gian bắt đầu** (theo múi giờ Việt Nam `UTC+7`) và **Thời gian đệm nghỉ dọn phòng** (`BufferMinutes`, mặc định `15` phút).
3. Nhập bảng giá vé cho từng loại ghế của suất chiếu (mặc định: `STANDARD = 75.000 đ`, `VIP = 95.000 đ`, `COUPLE = 180.000 đ`).
4. Nhấn **Công bố & Mở bán**:
   - Hệ thống tự động kiểm tra chống trùng lịch trong cùng phòng chiếu (tính cả thời gian phim + thời gian đệm).
   - Nếu hợp lệ, hệ thống tạo suất chiếu ở trạng thái `ON_SALE`, đóng băng chỉ số sức chứa `sellable_capacity_snapshot` và khởi tạo nguyên tử toàn bộ trạng thái ghế `ShowtimeSeats (AVAILABLE)`.
5. **Đóng bán hoặc Hủy suất chiếu (`/management/showtimes/{id}`):**
   - Quản lý có thể chuyển suất chiếu sang `CLOSED` (ngừng nhận đặt vé mới) bất kỳ lúc nào.
   - Chỉ được phép chuyển sang `CANCELLED` nếu suất chiếu **chưa có đơn hàng nào đã thanh toán (`PAID`)**. Nếu đã có vé bán ra, nút hủy sẽ bị khóa để bảo vệ khách hàng.

### 4.4. Xem Báo cáo Doanh thu và Tỷ lệ Lấp đầy (`/management/reports`)
1. Mở `/management/reports` và lọc theo **Khoảng ngày chiếu**, **Phim** hoặc **Phòng chiếu**.
2. Hệ thống truy vấn trực tiếp từ SQL View `dbo.vw_ShowtimeSales` và hiển thị:
   - **Tổng doanh thu thực nhận (`Paid Revenue VND`):** Chỉ cộng từ các đơn hàng đã `PAID` (loại trừ hoàn toàn đơn chờ, đơn lỗi, đơn hết hạn hoặc giao dịch `NEEDS_REVIEW`).
   - **Tổng số vé đã bán (`Sold Seats / Paid Tickets`)** và số ghế đang giữ tạm thời (`Held Seats`).
   - **Tỷ lệ lấp đầy (`Occupancy %`):** Tính chính xác theo công thức $\text{SoldSeats} \times 100 / \text{SellableCapacitySnapshot}$.
