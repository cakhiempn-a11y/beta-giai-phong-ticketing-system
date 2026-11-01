# TỔNG HỢP KẾT QUẢ KIỂM THỬ VÀ NGHIỆM THU HỆ THỐNG (`TEST_ACCEPTANCE_SUMMARY`)

**Đề tài:** Phân tích, thiết kế và xây dựng hệ thống quản lý bán vé trực tuyến tại rạp chiếu phim Beta Giải Phóng  
**Tác giả:** Vũ Văn Khiêm (`11235581`) — Ngành Hệ thống thông tin quản lý (MIS)  
**Giảng viên hướng dẫn:** ThS. Nguyễn Anh Phương  
**Nhánh hoàn thiện:** `docs/be05c-final-delivery` (Baseline `main`: `964bd232479b220856e13d33abc1aa7d8a0b7666`)

---

## 1. Tổng quan Ma trận 9 Tầng Kiểm thử & Nghiệm thu

Hệ thống được xác minh qua 9 tầng kiểm thử độc lập, kết hợp giữa kiểm thử tự động trong bộ nhớ, kiểm thử tích hợp trên SQL Server 2022 thực, kiểm thử trình duyệt thực Playwright Chromium và kiểm định bất biến dữ liệu sau thực thi:

| Tầng | Phân loại kiểm thử | Công cụ / Môi trường thực thi | Số lượng ca / Kịch bản | Kết quả thực tế | Bằng chứng định danh |
|:---:|---|---|:---:|:---:|---|
| 1 | **Unit Tests** | xUnit (`net10.0`) | 42 tests | **42 / 42 PASS** | `PasswordHasherTests`, `QrTokenProtectorTests`, `WebhookSignatureTests`, `VietnamTimeTests`, ... |
| 2 | **SQL Server Integration Tests** | xUnit + SQL Server 2022 (`TestDatabaseFixture`) | 68 tests | **68 / 68 PASS** | DB cô lập `BetaGiaiPhong_Test_<guid12>` cho mỗi lớp test; bao gồm `SchemaConstraintTests`, `ReportingViewTests`, Domain Services |
| 3 | **Web Functional & Security Tests**| xUnit + `WebApplicationFactory<Program>` | 56 tests | **56 / 56 PASS** | `AuthAndRbacWebTests`, `CatalogAndSeatMapWebTests`, `OrderAndPaymentWebTests`, `StaffTicketScanWebTests`, `ManagementWebTests` |
| 4 | **Real-Browser E2E Tests** | Playwright (`@playwright/test` — Chromium) | 5 spec files (16 kịch bản E2E) | **5 / 5 PASS** | Chạy trên tiến trình Kestrel thật (`127.0.0.1:5199`) + DB E2E riêng `BetaGiaiPhong_E2E_<runId>` |
| 5 | **SQL Server Integrity Gate** | `BetaGiaiPhong.Runner verify` | 9 nhóm bất biến SQL | **9 / 9 PASS (`0` vi phạm)**| Chạy tự động trong CI và trong `e2e/global-teardown.ts` (Phase 0.1 Quality Gate) |
| 6 | **Security & Secret-Leak Audit** | Playwright E2E + xUnit + CI Artifact Guard | 12 kiểm tra bảo mật | **PASS** | Chống IDOR (`403`), RBAC (`401`/`403`), CSRF (`400`), HMAC (`401`), kiểm tra không lộ chuỗi 43 ký tự Base64URL `qr_token` |
| 7 | **Concurrency & Race Conditions**| xUnit Multi-thread + Playwright `Promise.all`| 6 bài toán tranh chấp | **PASS (`0` bán trùng)** | 20 luồng giữ cùng 1 ghế (`1` thắng, `19` `409`, `0` `5xx`), 5 lần replay webhook, 2 luồng quét vé đồng thời |
| 8 | **Restart, Recovery & Lifecycle Gate**| Playwright (`03-restart-recovery.spec.ts`) | 4 kịch bản phục hồi | **PASS** | Tắt/bật lại Kestrel, thu hồi đơn quá hạn, thu hồi quyền tức thời, kiểm thử lỗi Setup (Phase 0.2) & Teardown (Phase 0.1) |
| 9 | **Manual Physical Camera UAT** | Thiết bị di động / Webcam / Máy quét 2D | 12 kịch bản UAT | **`MANUAL UAT — NOT TESTED`** | Lập quy trình nghiệm thu thủ công tại [MANUAL_CAMERA_UAT.md](MANUAL_CAMERA_UAT.md) |

> **Tổng cộng kiểm thử tự động:** **166 / 166 xUnit tests PASS** + **5 / 5 Playwright Chromium E2E suites PASS** + **9 / 9 nhóm bất biến SQL Server PASS**.

---

## 2. Đối chiếu Kết quả GitHub Actions CI & Khắc phục Triệt để Điểm Không ổn định (Phase 0)

### 2.1. Lịch sử GitHub Actions CI đã xác minh
- **PR #6 (BE-05A):** Run ID `41274101433` — **SUCCESS** (3 jobs: `static-and-schema-guard`, `dotnet-build-and-test`, `runner-integrity-verification`).
- **PR #7 (BE-05B):** Run ID `41278913554` (Commit `64a6c11`) — **SUCCESS** (4/4 jobs PASS, bao gồm job `playwright-e2e` trên Ubuntu 24.04 + SQL Server 2022 service container).
- **Post-Merge `main` Push Run:** Run ID `41279361688` (Commit `964bd232479b220856e13d33abc1aa7d8a0b7666`) — 3 jobs đầu PASS (`166/166` xUnit tests + `9/9` SQL invariants), nhưng job `playwright-e2e` gặp lỗi tại `02-negative-and-security.spec.ts:291` (`Test 2.8`) khi thư viện `Html5Qrcode.scanFile` trên trình duyệt không giải mã được 1 ảnh PNG QR.

### 2.2. Phân tích nguyên nhân gốc rễ (Root Cause Analysis) và Sửa lỗi trong BE-05C Phase 0
1. **Nguyên nhân kỹ thuật của lỗi giải mã ảnh QR trong Run `41279361688`:**
   - Thư viện `QRCoder` khi gọi `qrCode.GetGraphic(10)` cho chuỗi Base64URL 43 ký tự (QR Version 3, `29x29` modules + quiet zone = `37x37` modules) tạo ra ảnh kích thước `370x370` px, tức mỗi module đen/trắng rộng **`10x10` px**.
   - Trong khi đó, bộ lọc nhị phân cục bộ `HybridBinarizer` của `ZXing` (bên dưới `html5-qrcode`) chia ảnh thành các khối **`8x8` px** để tính ngưỡng sáng tối. Khi kích thước 1 module (`10px`) lớn hơn kích thước khối (`8px`), một khối `8x8` nằm hoàn toàn bên trong vùng đen của finder pattern có phương sai bằng `0` và đôi khi bị phân ngưỡng sai tùy theo chuỗi ngẫu nhiên của `qr_token`.
2. **Giải pháp khắc phục triệt để trong BE-05C:**
   - **Tầng Backend (`src/BetaGiaiPhong.Web/Services/TicketQueryService.cs`):** Đổi `qrCode.GetGraphic(10)` thành **`qrCode.GetGraphic(6)`** (`246x246` px, mỗi module rộng **`6px < 8px`** block size của `HybridBinarizer`).
   - **Tầng Frontend (`src/BetaGiaiPhong.Web/Views/StaffCheckIn/Index.cshtml`):** Bổ sung cơ chế fallback đa tỷ lệ (multi-scale white-padded offscreen canvas `260px`, `360px`, `480px`) khi tải tệp ảnh QR lên trình duyệt.
3. **Các nâng cấp Cổng chất lượng (Quality Gate Fixes Phase 0.1 – 0.4):**
   - **Phase 0.1 (`e2e/global-teardown.ts`):** Nếu `Runner verify` trả mã thoát khác `0` hoặc thiếu chuỗi `INTEGRITY PASSED`, teardown ném ngoại lệ làm đỏ toàn bộ E2E CI nhưng vẫn luôn thực thi khối `finally` để xóa DB `BetaGiaiPhong_E2E_<runId>` và tệp trạng thái `e2e-state.json`. Được chứng minh tự động bởi **`Test 7.6`** trong `03-restart-recovery.spec.ts`.
   - **Phase 0.2 (`e2e/global-setup.ts`):** Lưu tệp trạng thái quyền sở hữu DB ngay sau khi `create-e2e-db` thành công (trước khi khởi chạy Kestrel); nếu quá trình khởi động Web hoặc kiểm tra `/health` thất bại, tự động gọi `drop-e2e-db` và xóa tệp trạng thái. Được chứng minh tự động bởi **`Test 7.7`** trong `03-restart-recovery.spec.ts`.
   - **Phase 0.3 (`e2e/tests/05-performance-load.spec.ts`):** Ghi nhận trung thực `sqlExceptions: 'NOT MEASURED'` và `deadlocks: 'NOT MEASURED'` (thay vì gán cứng `0`), đồng thời tách bạch `expectedConflicts409` (kết quả từ chối hợp lệ khi tranh chấp ghế) khỏi `http5xxErrors` (`0` lỗi máy chủ).
   - **Phase 0.4 (`e2e/tests/helpers.ts`, `e2e/playwright.config.ts`, `.github/workflows/ci.yml`):** Giải mã `qr_token_cipher` trong bộ nhớ để lấy đúng chuỗi 43 ký tự Base64URL (`QrTokenProtector.Issue()` format `/^[A-Za-z0-9_-]{43}$/`), kiểm tra khẳng định chuỗi này không bao giờ rò rỉ trong HTML DOM, JSON API hay console log mà không in token ra log; tắt tự động chụp screenshot/video/trace trên CI và bổ sung bước quét làm sạch artifact trước khi tải lên GitHub Actions.

---

## 3. Chi tiết 5 Bộ Kiểm thử Trình duyệt Thực (Playwright Chromium E2E)

| Tệp Spec | Các kịch bản kiểm thử chính | Thời gian chạy | Kết quả |
|---|---|:---:|:---:|
| `01-full-journey.spec.ts` | Hành trình 9 bước liên thông trên trình duyệt thực:<br/>1. **Manager** đăng nhập, tạo phim mới (`PUBLISHED`), tạo suất chiếu mới trong Phòng 01 (48 ghế) và cấu hình bảng giá (`75.000đ / 95.000đ / 180.000đ`).<br/>2. **Customer** truy cập trang chủ, lọc phim, mở sơ đồ ghế (kiểm tra cả viewport Mobile `390x844` và Desktop `1280x800`).<br/>3. **Customer** chọn ghế `A1`, giữ ghế nguyên tử (`HELD`), kiểm tra đồng hồ đếm ngược và tổng tiền `75.000 đ`.<br/>4. **Customer** thực hiện thanh toán mô phỏng thành công (`PAID`), nhận vé điện tử và tải ảnh QR PNG.<br/>5. Kiểm tra bảo mật `assertNoSecretLeakInText`: xác nhận chuỗi 43 ký tự `qr_token` giải mã từ DB không xuất hiện trong HTML DOM, JSON `/api/orders/{id}` hay console log.<br/>6. **Staff** đăng nhập quầy soát vé `/staff/check-in`, tải trực tiếp tệp ảnh QR PNG của khách lên trình duyệt để giải mã và xác nhận `HỢP LỆ — CHO PHÉP VÀO RẠP (ACCEPTED)`.<br/>7. **Staff** quét lại cùng ảnh QR PNG lần thứ hai và xác nhận bị từ chối với `ĐÃ SỬ DỤNG (ALREADY_USED)`.<br/>8. **Manager** mở báo cáo doanh thu `/management/reports`, đối chiếu khớp tuyệt đối với SQL View `dbo.vw_ShowtimeSales`: `1` đơn `PAID`, `1` vé bán, doanh thu `75.000 đ`, tỷ lệ lấp đầy `2.08%` (`1/48`). | ~18s | **PASS** |
| `02-negative-and-security.spec.ts`| Kiểm thử 8 kịch bản âm tính và bảo mật:<br/>- `2.1`: Chặn Manager tạo suất chiếu chồng lấn thời gian (tính cả 15 phút đệm).<br/>- `2.2`: Chặn Customer chọn quá 8 ghế (chọn 9 ghế).<br/>- `2.3`: Chặn Customer B giữ ghế đang được Customer A giữ (`409 Conflict`).<br/>- `2.4`: Gửi lặp 5 lần cùng một Webhook thanh toán thành công (kiểm tra tính lũy đẳng, chỉ sinh 1 vé).<br/>- `2.5`: Từ chối Webhook giả mạo chữ ký HMAC-SHA256 (`401 Unauthorized`).<br/>- `2.6`: Chống IDOR — Customer B truy cập đơn hàng/API/ảnh QR của Customer A nhận `403 Forbidden`.<br/>- `2.7`: Kiểm tra RBAC — `GUEST` và `CUSTOMER` truy cập `/management/*` hoặc `/staff/*` bị chặn (`401`/`403`).<br/>- `2.8`: Kiểm tra Staff tải ảnh QR của suất chiếu khác nhận `SAI SUẤT CHIẾU (WRONG_SHOWTIME)` và nhập mã giả mạo nhận `KHÔNG TÌM THẤY VÉ (NOT_FOUND)`. | ~22s | **PASS** |
| `03-restart-recovery.spec.ts` | Kiểm thử khả năng phục hồi và vòng đời cổng chất lượng:<br/>- `3.1`: Tạo đơn `PAID` và đơn `PENDING_PAYMENT` có hạn giữ ghế ngắn; tắt hoàn toàn tiến trình Kestrel, khởi động lại Kestrel trên cùng CSDL SQL Server; xác nhận đơn `PAID` + vé QR được bảo toàn 100%, trong khi đơn quá hạn tự động chuyển `EXPIRED` và trả ghế về `AVAILABLE`.<br/>- `3.2`: Vô hiệu hóa tài khoản / thu hồi quyền trong DB khi đang có phiên đăng nhập; xác nhận request kế tiếp bị thu hồi phiên tức thời.<br/>- `7.6` (Phase 0.1): Tạo DB tạm, bơm lỗi bất biến giả lập (`corrupt-e2e-db`), chạy `runGlobalTeardownLifecycle` và chứng minh hàm ném lỗi làm fail cổng chất lượng nhưng vẫn xóa sạch DB tạm và tệp state.<br/>- `7.7` (Phase 0.2): Giả lập lỗi health-check giữa chừng trong `runGlobalSetupLifecycle` và chứng minh DB vừa tạo được dọn dẹp tự động. | ~35s | **PASS** |
| `04-responsive-and-a11y.spec.ts` | Kiểm thử giao diện đáp ứng (Responsive) trên 3 kích thước màn hình (`1440x900` Desktop, `768x1024` Tablet, `390x844` Mobile) và khả năng tiếp cận (A11y): kiểm tra không vỡ khung chứa, bảng dữ liệu `.table-responsive`, nhãn `<label for="...">`, điều hướng bàn phím (`Tab`, `Enter`), trạng thái khóa nút gửi và kích thước hiển thị ảnh QR PNG ($\ge 120 \times 120$ px). | ~15s | **PASS** |
| `05-performance-load.spec.ts` | Kiểm thử tải đồng thời và đo độ trễ thực nghiệm trên 8 endpoint/kịch bản (bao gồm 30 yêu cầu song song tranh chấp cùng 1 ghế `POST /api/bookings/reserve`: 1 thắng `201`, 29 `409 Conflict`, 0 `5xx`). | ~25s | **PASS** |

---

## 4. Kết quả Kiểm định 9 Nhóm Bất biến Toàn vẹn SQL Server (`Runner verify`)

Sau khi toàn bộ các bài kiểm thử chạy xong, công cụ `BetaGiaiPhong.Runner verify` thực thi 9 truy vấn kiểm toán toàn vẹn trực tiếp trên SQL Server:

| Nhóm | Tên kiểm tra bất biến (`IntegrityChecks.cs`) | Điều kiện vi phạm được quét | Số vi phạm | Kết quả |
|:---:|---|---|:---:|:---:|
| 1 | `Wrong room/seat or invalid ShowtimeSeats status` | Ghế suất chiếu trỏ sai phòng chiếu hoặc `HELD`/`SOLD` thiếu `owner_order_id` | `0` | **PASS** |
| 2 | `Active overlapping showtimes in same room` | Hai suất chiếu `DRAFT`/`ON_SALE`/`CLOSED` giao thời gian (kể cả `buffer_minutes`) trong cùng phòng | `0` | **PASS** |
| 3 | `Invalid order totals or seat counts` | `seat_count != COUNT(OrderSeats)` hoặc `total_vnd != SUM(OrderSeats.unit_price_vnd)` | `0` | **PASS** |
| 4 | `Published showtimes missing active room seats` | Suất chiếu `ON_SALE`/`CLOSED` thiếu dòng `ShowtimeSeats` cho ghế `is_active = 1` | `0` | **PASS** |
| 5 | `Duplicate or invalid tickets per order/seat` | Đơn chưa `PAID` nhưng có vé, hoặc số vé khác `seat_count`, hoặc lộ token rõ | `0` | **PASS** |
| 6 | `Paid orders without matching SOLD seats` | Đơn `PAID` nhưng ghế không ở trạng thái `SOLD` hoặc trỏ sai `owner_order_id` | `0` | **PASS** |
| 7 | `Duplicate successful payments or unverified events` | Nhiều hơn 1 giao dịch `SUCCESS` cho cùng 1 đơn hoặc sự kiện trùng lặp | `0` | **PASS** |
| 8 | `Invalid ticket scan states` | Vé có lượt quét `ACCEPTED` nhưng trạng thái vé không phải `USED` hoặc thiếu `used_utc` | `0` | **PASS** |
| 9 | `Sales view mismatch against transactional tables` | Chênh lệch số vé, doanh thu hoặc tỷ lệ lấp đầy giữa `dbo.vw_ShowtimeSales` và các bảng gốc | `0` | **PASS** |

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
| **`CONCURRENT POST /api/bookings/reserve` (30 luồng tranh cùng 1 ghế)** | **30** | **1** (`201`) | **29** (`409`) | **0** | 245.15 ms | **275.88 ms** | 99.76 rps | `NOT MEASURED` | $\le 1.500\text{ ms}$, `0` bán trùng (**ĐẠT**) |

- **Các chỉ số tầng SQL Engine nội bộ:**
  - `sqlExceptions`: **`NOT MEASURED`** *(Kịch bản đo từ phía HTTP client hộp đen nên không gắn bộ đếm ngoại lệ nội bộ của SQL Server; được kiểm soát gián tiếp qua `http5xxErrors = 0` và `Runner verify = 9/9 PASS`)*.
  - `deadlocks`: **`NOT MEASURED`** *(Không gắn Extended Events trace cho SQL Deadlock graph trong bài test E2E; thứ tự khóa `Showtime -> ShowtimeSeats` tuần tự hóa các giao dịch mà không phát sinh lỗi `500`)*.

### 5.2. Tuyên bố Trung thực Học thuật về Hiệu năng & Thiết bị Vật lý
1. **Không tuyên bố SLA thương mại:** Các số liệu hiệu năng trên là **cơ sở đo thực nghiệm trên môi trường nguyên mẫu một máy chủ (single-node academic prototype baseline)** với độ trễ mạng nội bộ (`loopback`). Báo cáo không tuyên bố đây là cam kết SLA ở quy mô tải thương mại phân tán trên Internet.
2. **Trạng thái kiểm thử Camera vật lý và trình duyệt ngoài Chromium:**
   - Giải mã ảnh QR PNG trực tiếp trên trình duyệt Chromium (`Html5Qrcode.scanFile` + canvas fallback) và nhập mã thủ công đã được kiểm thử tự động và đạt **100% PASS**.
   - Việc quét mã QR qua **camera vật lý trên điện thoại thông minh (Chrome Android, Safari iOS), webcam máy tính hoặc đầu đọc mã vạch USB 2D** cũng như chạy trên trình duyệt Firefox/WebKit thực tế được phân loại trung thực là **`MANUAL UAT — NOT TESTED`** trong môi trường tự động hóa và được hướng dẫn quy trình nghiệm thu chi tiết tại [docs/delivery/MANUAL_CAMERA_UAT.md](MANUAL_CAMERA_UAT.md).
