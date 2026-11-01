# TỔNG HỢP KẾT QUẢ KIỂM THỬ VÀ NGHIỆM THU HỆ THỐNG (`TEST_ACCEPTANCE_SUMMARY`)

**Đề tài:** Phân tích, thiết kế và xây dựng hệ thống quản lý bán vé trực tuyến tại rạp chiếu phim Beta Giải Phóng  
**Tác giả:** Vũ Văn Khiêm (`11235581`) — Ngành Hệ thống thông tin quản lý (MIS)  
**Giảng viên hướng dẫn:** ThS. Nguyễn Anh Phương  
**Nhánh hoàn thiện:** `fix/rc1-final-stabilization` (Baseline `main`: `102e633275e040fcc57be8e6276bd171d79de0fa`)

---

## 1. Tổng quan Ma trận 9 Tầng Kiểm thử & Nghiệm thu

Hệ thống được xác minh qua 9 tầng kiểm thử độc lập, kết hợp giữa kiểm thử đơn vị trong bộ nhớ, kiểm thử tích hợp trên SQL Server 2022 thực, kiểm thử trình duyệt thực Playwright Chromium và kiểm định bất biến dữ liệu sau thực thi:

| Tầng | Phân loại kiểm thử | Công cụ / Môi trường thực thi | Số lượng ca / Kịch bản | Kết quả thực tế | Bằng chứng định danh thực tế (`tests/` & `e2e/`) |
|:---:|---|---|:---:|:---:|---|
| 1 | **Unit Tests** | xUnit (`net10.0`) | **10 tests** | **10 / 10 PASS** | `MockPaymentSecurityTests` (4), `NavigationPropertyMappingTests` (3), `QrTokenProtectorTests` (3) |
| 2 | **SQL Server Integration & Concurrency Tests** | xUnit + SQL Server 2022 (`TestDatabaseFixture`) | **35 tests** | **35 / 35 PASS** | DB cô lập `BetaGiaiPhong_Test_<guid12>`: `BookingConcurrencyTests` (6), `PaymentWorkflowTests` (6), `QrScanningTests` (5), `ReportingAndIntegrityTests` (7), `ReservationExpiryWorkerTests` (2), `TestDatabaseSafetyTests` (9) |
| 3 | **Web Functional & Security Tests** | xUnit + `WebApplicationFactory<Program>` | **121 tests** | **121 / 121 PASS** | `AuthenticationAndAuthorizationWebTests` (11), `BookingAndOrderWebTests` (11), `ManagementWebTests` (30), `MockPaymentAndTicketWebTests` (21), `PublicCatalogWebTests` (6), `StaffCheckInWebTests` (18), `WebSecurityTests` (24) |
| 4 | **Real-Browser E2E Tests** | Playwright (`@playwright/test` — Chromium) | **5 spec files** | **5 / 5 PASS** | Chạy trên tiến trình Kestrel thật (`127.0.0.1:5199`) + DB E2E riêng `BetaGiaiPhong_E2E_<runId>` với `retries: 0` |
| 5 | **SQL Server Integrity Gate** | `BetaGiaiPhong.Runner verify` | **9 nhóm bất biến SQL** | **9 / 9 PASS (`0` vi phạm)** | Thực thi `IntegrityChecksAsync` trong `src/BetaGiaiPhong.Runner/Program.cs` (dòng 181–206) và trong `e2e/global-teardown.ts` |
| 6 | **Security & Secret-Leak Audit** | Playwright E2E + xUnit + CI Artifact Guard | **24+ kiểm tra bảo mật** | **PASS** | Chống IDOR (`403`), RBAC (`401`/`403`), CSRF (`400`), HMAC (`401`), kiểm tra không lộ chuỗi 43 ký tự Base64URL `qr_token` |
| 7 | **Concurrency & Race Conditions** | xUnit Multi-thread + Playwright `Promise.all` | **6 bài toán tranh chấp** | **PASS (`0` bán trùng)** | 20–30 luồng giữ cùng 1 ghế (`1` thắng, `19/29` `409`, `0` `5xx`), 5 lần replay webhook, 2 luồng quét vé đồng thời |
| 8 | **Restart, Recovery & Lifecycle Gate** | Playwright (`03-restart-recovery.spec.ts`) | **4 kịch bản phục hồi** | **PASS** | Tắt/bật lại Kestrel, thu hồi đơn quá hạn, thu hồi quyền tức thời, retry mạng sau lỗi kết nối, kiểm thử lỗi Setup (`7.7`) & Teardown (`7.6`) |
| 9 | **Manual Physical Camera UAT** | Thiết bị di động / Webcam / Máy quét 2D | **12 kịch bản UAT** | **`MANUAL UAT — NOT TESTED`** | Lập quy trình nghiệm thu thủ công tại [MANUAL_CAMERA_UAT.md](MANUAL_CAMERA_UAT.md) |

> **Tổng cộng kiểm thử tự động:** **166 / 166 xUnit tests PASS** (`10` Unit + `35` SQL Integration + `121` Web Integration) + **5 / 5 Playwright Chromium E2E suites PASS** + **9 / 9 nhóm bất biến SQL Server PASS**.

---

## 2. Đối chiếu Kết quả GitHub Actions CI & Phân tích Khắc phục Lỗi Quét Ảnh QR (RC-01 Phase 0 & Phase 1)

### 2.1. Lịch sử GitHub Actions CI đã xác minh
- **PR #6 (BE-05A):** Run ID `41274101433` — **SUCCESS** (3 jobs).
- **PR #7 (BE-05B):** Run ID `41278913554` — **SUCCESS** (4/4 jobs).
- **Post-Merge `main` Push Run (BE-05B):** Run ID `41279361688` — xUnit (`166/166`) và `Runner verify` (`9/9`) PASS, nhưng job `playwright-e2e` thất bại tại `02-negative-and-security.spec.ts:291` (`Expected: "SAI SUẤT CHIẾU"`, `Received: "Chờ quét..."`, timeout 15s).
- **PR #8 (BE-05C):** Run ID `41285460717` — **SUCCESS** (4/4 jobs).
- **Post-Merge `main` Push Run (BE-05C):** Run ID `41285939537` — xUnit (`166/166`) và `Runner verify` (`9/9`) PASS, nhưng job `playwright-e2e` thất bại tại `03-restart-recovery.spec.ts:241` (`Expected: "LỖI KẾT NỐI"`, `Received: "Chờ quét..."`, timeout 15s).

### 2.2. Phân tích 5 Nguyên nhân Gốc rễ (Root Cause Analysis) trong RC-01
Qua kiểm tra mã nguồn của `src/BetaGiaiPhong.Web/wwwroot/lib/html5-qrcode/html5-qrcode.min.js`, `TicketQueryService.cs` và `Views/StaffCheckIn/Index.cshtml`, nguyên nhân khiến việc tải ảnh QR lên trình duyệt đôi khi bị treo ở trạng thái `"Chờ quét..."` gồm 5 yếu tố kết hợp:
1. **QR Version 4 dày đặc module do `ECCLevel.Q`:** Chuỗi `qr_token` dài 43 ký tự Base64URL chữ hoa/thường lẫn lộn được mã hóa ở chế độ Byte (8-bit). Với `ECCLevel.Q`, dung lượng tối đa của QR Version 3 là 39 byte nên `QRCoder` phải nâng lên **QR Version 4 (`33x33` + 8 quiet zone = `41x41` modules)**, làm ma trận điểm ảnh dày hơn.
2. **Thu nhỏ ảnh theo tỷ lệ lẻ (`Non-integer downsampling`) do kích thước `#reader` (`265px`):** Trong `Views/StaffCheckIn/Index.cshtml`, phần tử `<div id="reader">` nằm cùng hàng flex với `#camera-placeholder` nên `clientWidth` chỉ đạt ~`265px`. Khi `Html5Qrcode.scanFile` gọi `computeCanvasDrawConfig(img.width, img.height, 265, 300)`, mọi ảnh lớn hơn `265px` bị nội suy thu nhỏ theo tỷ lệ thập phân lẻ (`265 / width`), làm nhòe biên đen–trắng của các module QR.
3. **Bộ giải mã ZXing bên trong `html5-qrcode` tắt `TRY_HARDER` và bật toàn bộ 17 định dạng mã vạch:** Mặc định `ZXingHtml5QrcodeShim` thiết lập `DecodeHintType.TRY_HARDER = false` (`hints.set(3, !1)`).
4. **Khối `catch` chỉ gọi `alert()` mà không cập nhật `#scanStatusBadge`:** Khi giải mã thất bại, `Index.cshtml` gọi `alert(...)` (gây treo hoặc bị đóng mà không đổi trạng thái UI), khiến `#scanStatusBadge` giữ nguyên chữ `"Chờ quét..."` cho đến khi hết hạn 15 giây. Ngoài ra, một khối mã chết `if (false)` còn tồn tại trong trình xử lý sự kiện tải file.
5. **Biến `isTicketAccepted` chặn lần tải file thứ hai liên tiếp:** Sau một lượt quét hợp lệ (`ACCEPTED`), nếu nhân viên chọn tải ngay một tệp ảnh QR tiếp theo mà chưa bấm nút `"Quét vé tiếp theo"`, hàm `submitScan` thoát sớm do `isTicketAccepted === true`.

### 2.3. Giải pháp Kỹ thuật Tất định đã Triển khai trong RC-01
1. **`TicketQueryService.GenerateQrCodePngBytesAsync` (`src/BetaGiaiPhong.Web/Services/TicketQueryService.cs`):**
   - Chuyển mức sửa lỗi từ `ECCLevel.Q` sang **`ECCLevel.M`** (đủ chứa 43 byte trong **QR Version 3: `29x29` + 8 quiet zone = `37x37` modules**) và kết xuất với **`qrCode.GetGraphic(7)`** (`259x259` px, kích thước module `7px < 8px` block size của `ZXing HybridBinarizer`).
2. **`Views/StaffCheckIn/Index.cshtml`:**
   - Bổ sung container offscreen chuyên dụng `<div id="qrFileScratch">` (`640x640` px) tách biệt hoàn toàn với `#reader`.
   - Khởi tạo `Html5Qrcode("qrFileScratch", { formatsToSupport: [Html5QrcodeSupportedFormats.QR_CODE], useBarCodeDetectorIfSupported: false })` và bật trực tiếp `DecodeHintType.TRY_HARDER` (`hints.set(3, true)`) trên cả `primaryDecoder` và `secondaryDecoder`.
   - Cài đặt `decodeQrFileDeterministically(file)` vẽ ảnh lên các canvas 2D đệm trắng sạch ở kích thước gốc và các bội số module (`185`, `222`, `259`, `296`, `370`, `246`, `328` px) rồi gọi trực tiếp `scanner.qrcode.decodeRobustlyAsync(canvas)`.
   - Xóa bỏ khối mã chết `if (false)`, tự động reset `isTicketAccepted = false` khi chọn tệp mới, bổ sung telemetry chẩn đoán an toàn `emitScanTelemetry(stage, meta)` (tuyệt đối không ghi token/hash/key), và hiển thị trạng thái **`"ẢNH QR LỖI"`** (`INVALID_QR_IMAGE`) trên `#scanStatusBadge` khi tệp tải lên không phải ảnh QR hợp lệ.

---

## 3. Chi tiết 5 Bộ Kiểm thử Trình duyệt Thực (Playwright Chromium E2E)

| Tệp Spec | Các kịch bản kiểm thử chính | Thời gian chạy | Kết quả |
|---|---|:---:|:---:|
| `01-full-journey.spec.ts` | Hành trình 9 bước liên thông trên trình duyệt thực (`Manager -> Customer -> Staff -> Manager`), bao gồm tải ảnh QR PNG `259x259` px lên `/staff/check-in` nhận `HỢP LỆ (ACCEPTED)`, quét lại nhận `ĐÃ SỬ DỤNG (ALREADY_USED)`, kiểm tra không rò rỉ chuỗi 43 ký tự `qr_token` và đối chiếu báo cáo doanh thu `dbo.vw_ShowtimeSales` (`1/48 = 2.08%`, `75.000 đ`). | ~18s | **PASS** |
| `02-negative-and-security.spec.ts` | Kiểm thử âm tính, bảo mật và chuỗi tải tệp QR liên tiếp:<br/>- `2.1`: Chặn Manager tạo suất chiếu chồng lấn thời gian (tính cả 15 phút đệm).<br/>- `2.2`: Chặn Customer chọn quá 8 ghế (chọn 9 ghế).<br/>- `2.3`: Chặn Customer B giữ ghế đang được Customer A giữ (`409 Conflict`).<br/>- `2.4`: Gửi lặp 5 lần cùng một Webhook thanh toán thành công (lũy đẳng, chỉ sinh 1 vé).<br/>- `2.5`: Từ chối Webhook giả mạo chữ ký HMAC-SHA256 (`401 Unauthorized`).<br/>- `2.6`: Chống IDOR — Customer B truy cập đơn hàng/API/ảnh QR của Customer A nhận `403 Forbidden`.<br/>- `2.7`: Kiểm tra RBAC — `GUEST` và `CUSTOMER` truy cập `/management/*` hoặc `/staff/*` bị chặn.<br/>- `2.8` & `6.2b`: Tải ảnh QR sai suất chiếu (`SAI SUẤT CHIẾU`), nhập mã không tồn tại (`KHÔNG TÌM THẤY VÉ`), tải ảnh PNG 1x1 không có QR (`ẢNH QR LỖI`), tải tệp `.png` hỏng (`ẢNH QR LỖI`), đổi suất chiếu trên dropdown và tải liên tiếp ảnh QR hợp lệ (`HỢP LỆ` $\rightarrow$ `ĐÃ SỬ DỤNG`) không cần tải lại trang. | ~20s | **PASS** |
| `03-restart-recovery.spec.ts` | Kiểm thử khả năng phục hồi và vòng đời cổng chất lượng:<br/>- `3.1`: Tắt/khởi động lại tiến trình Kestrel trên cùng CSDL SQL Server; xác nhận đơn `PAID` + vé QR được bảo toàn 100%, đơn quá hạn tự động chuyển `EXPIRED` và trả ghế về `AVAILABLE`.<br/>- `3.2`: Giả lập lỗi mạng tạm thời (`route.abort`) khi tải ảnh QR lên `/staff/check-in` $\rightarrow$ hiển thị `"LỖI KẾT NỐI"`, bấm `"Thử lại kết nối"` $\rightarrow$ chuyển sang `"HỢP LỆ"` (`ACCEPTED`).<br/>- `3.3`: Vô hiệu hóa tài khoản / thu hồi quyền trong DB khi đang có phiên đăng nhập; xác nhận request kế tiếp bị thu hồi phiên tức thời.<br/>- `7.6` & `7.7`: Chứng minh cổng chất lượng Teardown (khi vi phạm bất biến SQL) và Setup (khi lỗi health-check) luôn xóa sạch DB tạm. | ~32s | **PASS** |
| `04-responsive-and-a11y.spec.ts` | Kiểm thử giao diện đáp ứng (Responsive) trên 3 kích thước màn hình (`1440x900` Desktop, `768x1024` Tablet, `390x844` Mobile) và khả năng tiếp cận (A11y). | ~14s | **PASS** |
| `05-performance-load.spec.ts` | Kiểm thử tải đồng thời và đo độ trễ thực nghiệm trên 8 endpoint/kịch bản (bao gồm 30 yêu cầu song song tranh chấp cùng 1 ghế `POST /api/bookings/reserve`: 1 thắng, 29 `409 Conflict`, 0 `5xx`). | ~22s | **PASS** |

---

## 4. Kết quả Kiểm định 9 Nhóm Bất biến Toàn vẹn SQL Server (`Runner verify`)

Công cụ `src/BetaGiaiPhong.Runner/Program.cs` (`IntegrityChecksAsync`, dòng 181–206) thực thi chính xác 9 câu truy vấn kiểm toán toàn vẹn trực tiếp trên SQL Server sau mỗi chu kỳ kiểm thử:

| STT | Tên kiểm tra trong `Runner/Program.cs` | Câu truy vấn SQL thực tế (`IntegrityChecksAsync`) | Số vi phạm | Kết quả |
|:---:|---|---|:---:|:---:|
| 1 | `Wrong room/seat` | `SELECT COUNT_BIG(*) FROM dbo.ShowtimeSeats ss JOIN dbo.Seats s ON s.seat_id=ss.seat_id JOIN dbo.Showtimes st ON st.showtime_id=ss.showtime_id WHERE s.room_id<>st.room_id OR ss.room_id<>st.room_id` | `0` | **PASS** |
| 2 | `SOLD seat without PAID order` | `SELECT COUNT_BIG(*) FROM dbo.ShowtimeSeats ss LEFT JOIN dbo.Orders o ON o.order_id=ss.owner_order_id WHERE ss.state='SOLD' AND (o.order_id IS NULL OR o.status<>'PAID')` | `0` | **PASS** |
| 3 | `PAID orders missing tickets` | `SELECT COUNT_BIG(*) FROM dbo.Orders o OUTER APPLY (SELECT COUNT_BIG(*) n FROM dbo.Tickets t WHERE t.order_id=o.order_id) x WHERE o.status='PAID' AND x.n<>o.seat_count` | `0` | **PASS** |
| 4 | `Order price snapshot mismatch` | `SELECT COUNT_BIG(*) FROM dbo.Orders o OUTER APPLY (SELECT COALESCE(SUM(os.unit_price_vnd),0) v FROM dbo.OrderSeats os WHERE os.order_id=o.order_id) x WHERE x.v<>o.subtotal_vnd` | `0` | **PASS** |
| 5 | `Late payment improperly SUCCESS` | `SELECT COUNT_BIG(*) FROM dbo.Payments p JOIN dbo.Orders o ON o.order_id=p.order_id WHERE p.status='SUCCESS' AND o.status IN ('EXPIRED','CANCELLED')` | `0` | **PASS** |
| 6 | `Dead order still holds seat` | `SELECT COUNT_BIG(*) FROM dbo.ShowtimeSeats ss JOIN dbo.Orders o ON o.order_id=ss.owner_order_id WHERE ss.state='HELD' AND o.status IN ('CANCELLED','EXPIRED','FAILED','NEEDS_REVIEW')` | `0` | **PASS** |
| 7 | `Cancelled showtime with pending order` | `SELECT COUNT_BIG(*) FROM dbo.Orders o JOIN dbo.Showtimes s ON s.showtime_id=o.showtime_id WHERE s.status='CANCELLED' AND o.status='PENDING_PAYMENT'` | `0` | **PASS** |
| 8 | `Archived movie with bookable showtime` | `SELECT COUNT_BIG(*) FROM dbo.Showtimes s JOIN dbo.Movies m ON m.movie_id=s.movie_id WHERE m.status='ARCHIVED' AND s.status='ON_SALE' AND s.start_utc > SYSUTCDATETIME()` | `0` | **PASS** |
| 9 | `Ticket issued on unpaid order or cancelled showtime` | `SELECT COUNT_BIG(*) FROM dbo.Tickets t JOIN dbo.Orders o ON o.order_id=t.order_id JOIN dbo.Showtimes s ON s.showtime_id=t.showtime_id WHERE o.status<>'PAID' OR s.status='CANCELLED'` | `0` | **PASS** |

---

## 5. Báo cáo Cơ sở Hiệu năng Thực nghiệm (Performance Baseline) và Tuyên bố Giới hạn

### 5.1. Số liệu đo thực tế (`e2e/.tmp/benchmark-results.json`)
- **Môi trường đo:** Máy trạm phát triển Windows 11 (`Intel/AMD x64`), .NET 10 Kestrel (`Release` build), SQL Server 2022 cục bộ, thực thi từ Playwright Node.js runner (`05-performance-load.spec.ts`).

| Endpoint / Kịch bản đo | Số Request | Thành công (`2xx/3xx`) | Xung đột hợp lệ (`expectedConflicts409`) | Lỗi máy chủ (`http5xxErrors`) | Độ trễ TB (`avgLatencyMs`) | Độ trễ `p95LatencyMs` | Thông lượng (`throughputRps`) | `sqlExceptions` / `deadlocks` | Chỉ tiêu SRS (`NFR-01/02/03`) |
|---|:---:|:---:|:---:|:---:|---:|---:|---:|:---:|:---:|
| `GET /movies (Catalog)` | 25 | 25 | 0 | **0** | 9.77 ms | **12.74 ms** | 102.24 rps | `NOT MEASURED` | $\le 1.000\text{ ms}$ (**ĐẠT**) |
| `GET /showtimes/{id}/seats (Seat Map)` | 25 | 25 | 0 | **0** | 11.48 ms | **18.04 ms** | 86.94 rps | `NOT MEASURED` | $\le 1.000\text{ ms}$ (**ĐẠT**) |
| `POST /api/bookings/reserve (Valid Hold)` | 5 | 5 | 0 | **0** | 74.77 ms | **121.76 ms** | 13.38 rps | `NOT MEASURED` | $\le 1.500\text{ ms}$ (**ĐẠT**) |
| `POST /orders/{id}/demo-pay (Checkout & Ticket Issue)` | 5 | 5 | 0 | **0** | 329.33 ms | **492.36 ms** | 3.04 rps | `NOT MEASURED` | $\le 1.500\text{ ms}$ (**ĐẠT**) |
| `GET /tickets/{ticketId}/qr (QR PNG Generation)` | 20 | 20 | 0 | **0** | 10.95 ms | **13.38 ms** | 91.32 rps | `NOT MEASURED` | $\le 1.000\text{ ms}$ (**ĐẠT**) |
| `POST /api/staff/tickets/scan (Staff Verification)` | 20 | 20 | 0 | **0** | 15.20 ms | **16.06 ms** | 65.78 rps | `NOT MEASURED` | $\le 1.500\text{ ms}$ (**ĐẠT**) |
| `GET /management/reports (Sales Dashboard)` | 20 | 20 | 0 | **0** | 52.16 ms | **188.34 ms** | 19.17 rps | `NOT MEASURED` | $\le 1.000\text{ ms}$ (**ĐẠT**) |
| **`CONCURRENT POST /api/bookings/reserve` (30 luồng tranh cùng 1 ghế)** | **30** | **1** (`200`) | **29** (`409`) | **0** | 245.15 ms | **275.88 ms** | 99.76 rps | `NOT MEASURED` | $\le 1.500\text{ ms}$, `0` bán trùng (**ĐẠT**) |

- **Các chỉ số tầng SQL Engine nội bộ:**
  - `sqlExceptions`: **`NOT MEASURED`** *(Kịch bản đo từ phía HTTP client hộp đen nên không gắn bộ đếm ngoại lệ nội bộ của SQL Server; được kiểm soát gián tiếp qua `http5xxErrors = 0` và `Runner verify = 9/9 PASS`)*.
  - `deadlocks`: **`NOT MEASURED`** *(Không gắn Extended Events trace cho SQL Deadlock graph trong bài test E2E; thứ tự khóa `Showtime -> ShowtimeSeats` tuần tự hóa các giao dịch mà không phát sinh lỗi `500`)*.

### 5.2. Tuyên bố Trung thực Học thuật về Hiệu năng & Thiết bị Vật lý
1. **Không tuyên bố SLA thương mại:** Các số liệu hiệu năng trên là **cơ sở đo thực nghiệm trên môi trường nguyên mẫu một máy chủ (single-node academic prototype baseline)** với độ trễ mạng nội bộ (`loopback`). Báo cáo không tuyên bố đây là cam kết SLA ở quy mô tải thương mại phân tán trên Internet.
2. **Trạng thái kiểm thử Camera vật lý và trình duyệt ngoài Chromium:**
   - Giải mã ảnh QR PNG trực tiếp trên trình duyệt Chromium (`#qrFileScratch` + `decodeQrFileDeterministically`) và nhập mã thủ công đã được kiểm thử tự động và đạt **100% PASS**.
   - Việc quét mã QR qua **camera vật lý trên điện thoại thông minh (Chrome Android, Safari iOS), webcam máy tính hoặc đầu đọc mã vạch USB 2D** cũng như chạy trên trình duyệt Firefox/WebKit thực tế được phân loại trung thực là **`MANUAL UAT — NOT TESTED`** trong môi trường tự động hóa và được hướng dẫn quy trình nghiệm thu chi tiết tại [docs/delivery/MANUAL_CAMERA_UAT.md](MANUAL_CAMERA_UAT.md).
