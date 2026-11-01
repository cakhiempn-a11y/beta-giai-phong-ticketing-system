# BÁO CÁO KIỂM TOÁN ỔN ĐỊNH HÓA CUỐI CÙNG & SẴN SÀNG PHÁT HÀNH (`RC1_FINAL_AUDIT_REPORT`)

**Đề tài:** Phân tích, thiết kế và xây dựng hệ thống quản lý bán vé trực tuyến tại rạp chiếu phim Beta Giải Phóng  
**Tác giả:** Vũ Văn Khiêm (`11235581`) — Ngành Hệ thống thông tin quản lý (MIS)  
**Giảng viên hướng dẫn:** ThS. Nguyễn Anh Phương  
**Nhánh thực hiện:** `fix/rc1-final-stabilization`  
**Baseline `main`:** `102e633275e040fcc57be8e6276bd171d79de0fa` (PR #1 – #8 đã hợp nhất)  
**Ngày hoàn thành:** 2026-11-01

---

## 1. Phân tích Nguyên nhân Gốc rễ Lỗi Quét Ảnh QR trên GitHub Actions CI (`Run 41279361688` & `Run 41285939537`)

### 1.1. Hiện tượng trên CI nhánh `main`
- **Run `41279361688` (BE-05B post-merge):** xUnit (`166/166`) và `Runner verify` (`9/9`) PASS; job `e2e-browser-tests` thất bại tại `e2e/tests/02-negative-and-security.spec.ts:291` (`Expected: "SAI SUẤT CHIẾU"`, `Received: "Chờ quét..."`, timeout 15 giây).
- **Run `41285939537` (BE-05C post-merge):** xUnit (`166/166`) và `Runner verify` (`9/9`) PASS; job `e2e-browser-tests` thất bại tại `e2e/tests/03-restart-recovery.spec.ts:241` (`Expected: "LỖI KẾT NỐI"`, `Received: "Chờ quét..."`, timeout 15 giây).

### 1.2. Năm Nguyên nhân Kỹ thuật Kết hợp (Root Causes)
Qua kiểm tra trực tiếp mã nguồn của `TicketQueryService.GenerateQrCodePngBytesAsync`, `Views/StaffCheckIn/Index.cshtml` và thư viện `wwwroot/lib/html5-qrcode/html5-qrcode.min.js` (`scanFileV2`, `computeCanvasDrawConfig`, `decodeRobustlyAsync`, `ZXingHtml5QrcodeShim`), 5 nguyên nhân gốc rễ sau đã được xác định:

1. **QR Version 4 dày đặc module do `ECCLevel.Q`:**
   - `QrTokenProtector.Issue()` sinh chuỗi Base64URL dài 43 ký tự chữ hoa/thường lẫn lộn (chế độ mã hóa Byte 8-bit).
   - Ở mức sửa lỗi `ECCLevel.Q`, dung lượng tối đa của QR Version 3 là 39 byte nên `QRCoder` phải nâng lên **QR Version 4 (`33x33` + 8 quiet zone = `41x41` modules)**. Với `GetGraphic(6)`, ảnh có kích thước `246x246` px và mật độ module cao hơn cần thiết.
2. **Thu nhỏ ảnh theo tỷ lệ thập phân lẻ (`Non-integer downsampling`) do kích thước `#reader` (`265px`):**
   - Trong `Views/StaffCheckIn/Index.cshtml`, phần tử `<div id="reader" style="width: 100%;"></div>` nằm chung trong một flex container cùng với `#camera-placeholder`, khiến `#reader.clientWidth` chỉ đạt ~`265px`.
   - Khi `tempScanner.scanFile(file, false)` chạy, hàm `computeCanvasDrawConfig(img.width, img.height, 265, 300)` thu nhỏ mọi ảnh có bề rộng lớn hơn `265px` theo tỷ lệ lẻ (`265 / width`), làm nhòe ranh giới đen–trắng của các module QR trên canvas.
3. **Bộ giải mã ZXing bên trong `html5-qrcode.min.js` tắt `TRY_HARDER` và bật toàn bộ 17 định dạng barcode:**
   - Mặc định lớp `ZXingHtml5QrcodeShim` đặt `hints.set(DecodeHintType.TRY_HARDER, false)` (`key 3 = false`) và quét cả 17 định dạng 1D/2D.
4. **Khối `catch` chỉ gọi `alert()` mà không cập nhật `#scanStatusBadge` + Khối mã chết `if (false)`:**
   - Khi giải mã ảnh thất bại, trình xử lý sự kiện chỉ gọi `alert("Không tìm thấy mã QR hợp lệ...")` mà không cập nhật `#scanStatusBadge`, khiến huy hiệu trạng thái bị kẹt vĩnh viễn ở `"Chờ quét..."` cho đến khi Playwright hết hạn 15 giây (trong `03-restart-recovery.spec.ts` thậm chí chưa đăng ký `staffPage.on('dialog', ...)`). Đồng thời trong `Index.cshtml` còn tồn tại một khối mã chết `if (false)`.
5. **Biến cờ `isTicketAccepted` chặn lần tải tệp QR tiếp theo:**
   - Sau khi một vé hợp lệ được chấp nhận (`ACCEPTED`), `isTicketAccepted` giữ giá trị `true` và chặn các lần tải tệp tiếp theo nếu người dùng không bấm nút `"Quét vé tiếp theo"` trước.

---

## 2. Giải pháp Kỹ thuật Tất định đã Triển khai (Phase 1)

| Thành phần | Tệp sửa đổi | Nội dung khắc phục chi tiết |
|---|---|---|
| **Tạo ảnh QR PNG (`Backend`)** | [`src/BetaGiaiPhong.Web/Services/TicketQueryService.cs`](file:///d:/Projects/BetaGiaiPhong/src/BetaGiaiPhong.Web/Services/TicketQueryService.cs) | Chuyển mức sửa lỗi từ `ECCLevel.Q` sang **`ECCLevel.M`** (đủ chứa 43 byte trong **QR Version 3: `29x29` + 8 quiet zone = `37x37` modules**) và kết xuất với **`qrCode.GetGraphic(7)`** (`259x259` px, kích thước module `7px < 8px` block size của bộ lọc `ZXing HybridBinarizer`). |
| **Giải mã tệp ảnh QR & UI lỗi (`Frontend`)** | [`src/BetaGiaiPhong.Web/Views/StaffCheckIn/Index.cshtml`](file:///d:/Projects/BetaGiaiPhong/src/BetaGiaiPhong.Web/Views/StaffCheckIn/Index.cshtml) | 1. Thêm vùng chứa offscreen cố định `<div id="qrFileScratch">` (`640x640` px) tách biệt hoàn toàn với `#reader`.<br/>2. Khởi tạo `Html5Qrcode("qrFileScratch", { formatsToSupport: [Html5QrcodeSupportedFormats.QR_CODE], useBarCodeDetectorIfSupported: false })` và bật trực tiếp `DecodeHintType.TRY_HARDER` (`hints.set(3, true)`) trên cả `primaryDecoder` và `secondaryDecoder`.<br/>3. Xây dựng `decodeQrFileDeterministically(file)` vẽ ảnh lên các canvas 2D đệm trắng sạch ở kích thước gốc có viền trắng (`native_pad48`) và các bội số module (`185`, `222`, `259`, `296`, `370`, `246`, `328` px), gọi trực tiếp `scanner.qrcode.decodeRobustlyAsync(canvas)` trước khi fallback sang `scanner.scanFile(file, false)`.<br/>4. Xóa bỏ hoàn toàn khối mã chết `if (false)`.<br/>5. Khi tệp ảnh không chứa mã QR hoặc bị hỏng, gọi `renderResult({ outcome: "INVALID_QR_IMAGE", ... })` để chuyển `#scanStatusBadge` sang **`"ẢNH QR LỖI"`** (không bao giờ treo ở `"Chờ quét..."`).<br/>6. Tự động đặt lại `isTicketAccepted = false` khi chọn tệp mới hoặc gửi form nhập tay.<br/>7. Bổ sung hàm ghi log chẩn đoán an toàn `emitScanTelemetry(stage, meta)` (`[QR-SCAN-STAGE] ...`) chỉ ghi tên giai đoạn và kích thước ảnh, tuyệt đối không ghi `qr_token`, hash hay khóa mật mã. |
| **Kiểm thử tự động (`E2E & xUnit`)** | [`e2e/tests/02-negative-and-security.spec.ts`](file:///d:/Projects/BetaGiaiPhong/e2e/tests/02-negative-and-security.spec.ts), [`e2e/tests/03-restart-recovery.spec.ts`](file:///d:/Projects/BetaGiaiPhong/e2e/tests/03-restart-recovery.spec.ts), [`tests/BetaGiaiPhong.Tests/WebTests/StaffCheckInWebTests.cs`](file:///d:/Projects/BetaGiaiPhong/tests/BetaGiaiPhong.Tests/WebTests/StaffCheckInWebTests.cs) | - Bổ sung mục `6.2b` trong `02-negative-and-security.spec.ts`: tải ảnh PNG 1x1 không có QR (`"ẢNH QR LỖI"`), tải tệp `.png` hỏng (`"ẢNH QR LỖI"`), tải liên tiếp ảnh QR khi đang ở suất chiếu B (`"SAI SUẤT CHIẾU"`), đổi `#showtimeSelect` sang suất chiếu A, tải ảnh QR nhận `"HỢP LỆ"` (`ACCEPTED`), tải lại ngay lập tức nhận `"ĐÃ SỬ DỤNG"` (`ALREADY_USED`) mà không cần reload trang.<br/>- Trong `03-restart-recovery.spec.ts`: kiểm tra tường minh sau khi tải ảnh QR lúc đứt mạng (`"LỖI KẾT NỐI"`) và nhấn `"Thử lại kết nối"`, trạng thái chuyển sang `"HỢP LỆ"` (`"Soát vé thành công"`).<br/>- Trong `StaffCheckInWebTests.TC_WEB_STAFF_18`: kiểm tra sự hiện diện của `qrFileScratch`, `decodeQrFileDeterministically`, `INVALID_QR_IMAGE` và đảm bảo không ghi log token thô. |

---

## 3. Kết quả Kiểm chứng Độ Tin cậy Lặp lại (Phase 4 Verification Results)

Toàn bộ các cổng chất lượng đã được thực thi trên nhánh `fix/rc1-final-stabilization` với `retries = 0`:

| Cổng kiểm tra | Lệnh thực thi | Kết quả |
|---|---|---|
| **1. Khôi phục & Biên dịch Release** | `dotnet restore BetaGiaiPhong.slnx` & `dotnet build BetaGiaiPhong.slnx -c Release --no-restore` | **PASS** (`0 Warning(s)`, `0 Error(s)`) |
| **2. Kiểm tra tĩnh Schema SQL** | `python scripts/static_schema_checks.py` | **PASS** (`17 tables, 116 columns, 22 FKs`) |
| **3. Kiểm tra EF Core Model Drift** | `dotnet ef migrations has-pending-model-changes --project src/BetaGiaiPhong.Data --startup-project src/BetaGiaiPhong.Data` | **PASS** (`No changes have been made to the model since the last migration.`) |
| **4. Quét lỗ hổng bảo mật NuGet** | `dotnet list BetaGiaiPhong.slnx package --vulnerable --include-transitive` | **PASS** (`0` gói có lỗ hổng trên cả 4 dự án) |
| **5. Bộ kiểm thử xUnit (.NET 10)** | `dotnet test BetaGiaiPhong.slnx -c Release --no-build` | **PASS (`166 / 166` tests passed, `0` failed, `0` skipped)** |
| **6. Playwright E2E — Lượt chạy độc lập #1/3** | `npx playwright test --retries=0` (`BetaGiaiPhong_E2E_mvxkp1x3_fa384c`) | **PASS (`5 / 5` specs passed, `9 / 9` SQL invariants passed, DB dropped safely)** |
| **7. Playwright E2E — Lượt chạy độc lập #2/3** | `npx playwright test --retries=0` (`BetaGiaiPhong_E2E_mvxkrwxp_08838d`) | **PASS (`5 / 5` specs passed, `9 / 9` SQL invariants passed, DB dropped safely)** |
| **8. Playwright E2E — Lượt chạy độc lập #3/3** | `npx playwright test --retries=0` (`BetaGiaiPhong_E2E_mvxkuq14_6efa44`) | **PASS (`5 / 5` specs passed, `9 / 9` SQL invariants passed, DB dropped safely)** |

---

## 4. Danh mục Đối chiếu & Đồng bộ Tài liệu Bàn giao (Phase 2 & Phase 3 Reconciliation)

Toàn bộ tài liệu trong `docs/delivery/` và `docs/quality/` đã được rà soát và đồng bộ 100% với mã nguồn thực tế:

1. **Đồng bộ Danh mục Route, Controller, Action và Service (`SRS_IMPLEMENTATION_TRACEABILITY.md`, `ARCHITECTURE_AND_DATA_MODEL.md`):**
   - Cập nhật chính xác 9 MVC Controllers (`HomeController`, `AccountController`, `MoviesController`, `ShowtimesController`, `BookingsController`, `OrdersController`, `TicketsController`, `StaffCheckInController`, `ManagementController`) và 9 API Controllers (`MeApiController`, `MoviesApiController`, `ShowtimesApiController`, `BookingsApiController`, `OrdersApiController`, `PaymentsApiController`, `TicketsApiController`, `WebhooksApiController`, `StaffTicketsApiController`).
   - Loại bỏ hoàn toàn các route giả định không tồn tại (`/api/management/*`, `/api/auth/me`, `/api/catalog/movies`, `/api/showtimes/{id}/seat-map`, `/orders/{id}/tickets/{ticketId}/qr.png`) và thay bằng các route thực tế (`/api/me`, `/api/movies`, `/showtimes/{id}/seats`, `/api/showtimes/{id}/seats`, `/tickets/{ticketId}/qr`, `/orders/{id}/checkout`, `/orders/{id}/demo-pay`, `/orders/{id}/tickets`, `/api/webhooks/mock-payment` với header `X-Signature-SHA256`, `/api/staff/tickets/scan`).
   - Đồng bộ tên chính sách phân quyền (`RequireCustomer`, `RequireStaff`, `RequireManager`, `RequireAdmin`), tên Cookie (`.BetaGiaiPhong.Auth`) và tên dịch vụ (`AuthService`, `CatalogService`, `BookingQueryService`, `TicketQueryService`, `MovieManagementService`, `ManagementQueryService`).
2. **Đồng bộ Tên Hàm Kiểm thử xUnit Thực tế (`SRS_IMPLEMENTATION_TRACEABILITY.md`, `TEST_ACCEPTANCE_SUMMARY.md`, `THESIS_TECHNICAL_CONTENT.md`):**
   - Ánh xạ từng yêu cầu sang đúng tên lớp và tên hàm `[Fact]` thực tế trong `tests/BetaGiaiPhong.Tests/` (`TC_CON_01..06`, `TC_PAY_01..06`, `TC_QR_01..05`, `TC_INT_01..04`, `TC_REP_01..03`, `TC_WORKER_01..02`, `TC_SAFE_01..09`, `TC_WEB_AUTH_01..11`, `TC_WEB_BOOK_01..11`, `TC_MGMT_01..30`, `TC_WEB_PAY_01..21`, `TC_WEB_CAT_01..06`, `TC_WEB_STAFF_01..18`, `TC_WEB_SEC_01..24`).
   - Chuẩn hóa số lượng từng nhóm trong tổng số **166 bài kiểm thử xUnit**: **10 Unit Tests** + **35 SQL Server Integration & Concurrency Tests** + **121 Web Functional & Security Tests**.
3. **Đồng bộ Bảng 9 Bất biến Toàn vẹn SQL Server (`TEST_ACCEPTANCE_SUMMARY.md`):**
   - Thay thế bảng mô tả tóm tắt bằng chính xác **9 câu truy vấn SQL thực tế** trong `src/BetaGiaiPhong.Runner/Program.cs` (`IntegrityChecksAsync`, dòng 183–193):
     1. `Wrong room/seat`
     2. `SOLD seat without PAID order`
     3. `PAID orders missing tickets`
     4. `Order price snapshot mismatch`
     5. `Late payment improperly SUCCESS`
     6. `Dead order still holds seat`
     7. `Cancelled showtime with pending order`
     8. `Archived movie with bookable showtime`
     9. `Ticket issued on unpaid order or cancelled showtime`
4. **Chuẩn hóa Mẫu số Độ bao phủ Yêu cầu Chức năng (`SRS_IMPLEMENTATION_TRACEABILITY.md`, `BE_05C_REPORT.md`, `THESIS_TECHNICAL_CONTENT.md`):**
   - Xóa bỏ lỗi đánh máy `"45 mã FR"`, khẳng định rõ tổng số **54 mã FR (`52 P0 + 2 P1`)**.
   - Trong **52 yêu cầu P0**: có **46 `IMPLEMENTED`**, **5 `PARTIALLY IMPLEMENTED`** (`FR-SCH-01`, `FR-SCH-02`, `FR-SCH-08`, `FR-RPT-04`, `FR-AUD-02`) và **1 `NOT IMPLEMENTED`** (`FR-AUTH-02` — tự đăng ký tài khoản khách hàng). Trong **2 yêu cầu P1**: có **1 `PARTIALLY IMPLEMENTED`** (`FR-AUTH-06`) và **1 `OUT OF SCOPE`** (`FR-RPT-05`). Không tuyên bố 100% P0 đã cài đặt.
5. **Đồng bộ Hướng dẫn Cài đặt & Kịch bản Bảo vệ (`INSTALLATION_GUIDE.md`, `DEFENSE_DEMO_SCRIPT.md`, `USER_MANUAL.md`, `MANUAL_CAMERA_UAT.md`):**
   - Xóa bỏ các tham chiếu tới script không tồn tại (`02_run_web_dev.ps1`, `03_seed_checkin_demo.ps1`, `.dev-session.env.json`) và thay bằng các script thực tế (`scripts/01_prepare_dev_session.ps1`, `scripts/02_run_dev.ps1`, `scripts/static_schema_checks.py`) cùng cơ chế đồng bộ khóa từ `.NET User Secrets`.
   - Thay thế lệnh `Get-Random` bằng bộ sinh số ngẫu nhiên mật mã **`System.Security.Cryptography.RandomNumberGenerator` (CSPRNG)**.
   - Đồng bộ cổng ứng dụng theo `Properties/launchSettings.json` (`http://localhost:5145` và `https://localhost:7097`), biến môi trường kiểm thử `BGP_TEST_SQL_MASTER_CONNECTION` và độ dài tối thiểu của `BGP_DEMO_PASSWORD` ($\ge 12$ ký tự).
   - Tách `DEFENSE_DEMO_SCRIPT.md` thành **Chế độ 1: Full Journey Demo (12–15 phút)** (không chạy `seed-checkin-demo` để tránh đụng độ lịch chiếu Phòng 01 tại `now + 15m`) và **Chế độ 2: Quick Recovery / Short Demo (5 phút)** (chạy `seed-checkin-demo` để có sẵn suất chiếu và vé `PAID`).

---

## 5. Bảng Tổng hợp Trạng thái Đáp ứng Yêu cầu Cuối cùng (Final Requirement Status Summary)

| Nhóm hạng mục | Tổng số mã | `IMPLEMENTED` | `PARTIALLY IMPLEMENTED` | `NOT IMPLEMENTED` | `OUT OF SCOPE (P1)` | Ghi chú tổng hợp |
|---|---:|---:|---:|---:|---:|---|
| **Yêu cầu chức năng (`FR-*`)** | **54** (`52 P0 + 2 P1`) | **46** (`46 P0`) | **6** (`5 P0 + 1 P1`) | **1** (`FR-AUTH-02` — P0) | **1** (`FR-RPT-05` — P1) | 46/52 P0 hoàn chỉnh, 5/52 P0 một phần, 1/52 P0 chưa cài đặt (`FR-AUTH-02`). |
| **Quy tắc nghiệp vụ (`BR-*`)** | **18** | **18** | **0** | **0** | **0** | 100% quy tắc nghiệp vụ cốt lõi được bảo vệ ở cả Domain Service và SQL Server Constraints. |
| **Yêu cầu phi chức năng (`NFR-*`)** | **18** | **17** | **1** (`NFR-15`) | **0** | **0** | `NFR-15` đã kiểm thử tự động trên Chromium Desktop/Mobile; camera vật lý và Safari/Firefox thuộc diện Manual UAT (`NOT VERIFIED`). |
| **Quyết định thiết kế (`CR-*`)** | **9** | **9** | **0** | **0** | **0** | 100% quyết định kiến trúc v1.1 (`CR-01`..`CR-09`) đã thực thi và có test bảo vệ. |
| **Use Cases (`UC-*`)** | **16** | **11** | **4** (`UC-02, 10, 13, 14`) | **0** | **1** (`UC-16`) | Toàn bộ hành trình nghiệp vụ cốt lõi (`UC-01`, `03..09`, `11`, `12`, `15`) hoạt động đầu–cuối trên trình duyệt thực. |
| **Sơ đồ BPMN (`BPMN-*`)** | **6** | **6** | **0** | **0** | **0** | `BPMN-01`, `02A`, `02B`, `03`, `04`, `05` (trừ nhánh tùy chọn xuất CSV P1 ở cuối `BPMN-05`). |
| **Sơ đồ Sequence (`SD-*`)** | **14** | **12** | **2** (`SD-13, SD-14`) | **0** | **0** | `SD-01`..`SD-12` khớp hoàn toàn với luồng thực thi của ứng dụng; `SD-13`, `SD-14` đã có tầng DB/Middleware. |
| **Ca kiểm thử nghiệm thu (`TC-*`)** | **25** | **23** | **1** (`TC-06`) | **1** (`TC-02`) | **0** | 24/25 ca kiểm thử nghiệm thu trong SRS đã được tự động hóa bằng xUnit và Playwright E2E. |

---

## 6. Các Giới hạn Còn lại & Trạng thái Nghiệm thu Thủ công (Remaining Limitations & Manual UAT Status)

1. **`FR-AUTH-02` (`NOT IMPLEMENTED`):** Chưa xây dựng trang tự đăng ký tài khoản khách hàng công khai (`/account/register`); nguyên mẫu sử dụng các tài khoản được tạo sẵn có kiểm soát qua `DemoSeeder`.
2. **Các giao diện quản trị phụ trợ & tính năng P1 (`PARTIALLY IMPLEMENTED` / `OUT OF SCOPE`):**
   - `FR-SCH-01`, `FR-SCH-02`, `FR-SCH-08`: Đã có đầy đủ ràng buộc CSDL, dữ liệu seed, trang xem sơ đồ ghế phòng chiếu (`/management/rooms/{id}`) và cấu hình giá khi tạo suất chiếu; không mở form thêm/sửa phòng chiếu tự do hoặc sửa giá sau khi mở bán.
   - `FR-RPT-04`, `FR-AUD-02`: Báo cáo `/management/reports` đã tách bạch hoàn toàn doanh thu `PAID` và đếm riêng các trạng thái đơn, đồng thời có trang `/staff/check-in/history`; chưa tách trang tra cứu đơn hàng/audit log tổng quát độc lập cho Admin.
   - `FR-AUTH-06` (`P1`), `FR-RPT-05` (`P1`): Thuộc phạm vi ưu tiên `P1` (tầng xác thực `OnValidatePrincipal` thu hồi quyền tức thời đã hoàn thiện).
3. **Camera vật lý & Trình duyệt ngoài Chromium (`MANUAL UAT — NOT TESTED`):**
   - Việc quét mã QR qua ống kính quang học của điện thoại di động/webcam thực tế và kiểm thử trên Safari/Firefox thực địa được ghi nhận trung thực là **`MANUAL UAT — NOT TESTED`** và có quy trình kiểm thử 12 kịch bản tại [`docs/delivery/MANUAL_CAMERA_UAT.md`](../delivery/MANUAL_CAMERA_UAT.md).

---

## 7. Tuyên bố Sẵn sàng Phát hành (Release Readiness Statement)

Với **166/166 bài kiểm thử xUnit PASS**, **5/5 bộ kiểm thử trình duyệt thực Playwright Chromium E2E PASS qua 3 lượt chạy độc lập liên tiếp (`retries: 0`)**, **9/9 nhóm bất biến toàn vẹn SQL Server đạt `0` vi phạm**, **0 cảnh báo biên dịch**, **0 gói phụ thuộc có lỗ hổng bảo mật** và **hồ sơ tài liệu bàn giao đã được đối chiếu khớp 100% với mã nguồn thực tế**, nhánh `fix/rc1-final-stabilization` đã đạt tiêu chuẩn **Release Candidate (`v1.0.0-capstone-rc1`)**, sẵn sàng để đẩy lên GitHub, mở Pull Request cuối cùng và phục vụ bảo vệ đồ án tốt nghiệp.
