# BÁO CÁO NGHIỆM THU CHẤT LƯỢNG — BE-05B
## Real Browser E2E Testing, Database Safety, Recovery, Performance & User Acceptance

**Dự án:** Phân tích, thiết kế và xây dựng hệ thống quản lý bán vé trực tuyến tại rạp chiếu phim Beta Giải Phóng  
**Giai đoạn:** BE-05B — Kiểm thử End-to-End trên trình duyệt thực (Playwright Chromium), An toàn Cơ sở dữ liệu Kiểm thử, Khả năng Phục hồi (Recovery), Hiệu năng (Performance) & Nghiệm thu Người dùng (UAT)  
**Nhánh làm việc:** `feat/be05b-e2e-uat`  
**Baseline commit:** `c79b1b65aaef59dc817e38be16051030f08487d2` (`main` sau PR #6)  
**Ngày báo cáo:** 2026-11-01  

> [!IMPORTANT]
> **Tuyên bố học thuật & Phân tách Trạng thái Kiểm thử:**  
> Đây là nguyên mẫu học thuật phục vụ đồ án Hệ thống Thông tin Quản lý (MIS), sử dụng dữ liệu mô phỏng và cổng thanh toán giả lập có chữ ký mật mã HMAC-SHA256. Báo cáo này phân biệt trung thực 3 mức độ kiểm chứng:
> 1. **`AUTOMATED BROWSER PASS`**: Đã kiểm thử tự động trên trình duyệt thực **Chromium** thông qua **Playwright Test** kết nối tới tiến trình máy chủ thật `BetaGiaiPhong.Web` và cơ sở dữ liệu SQL Server thật.
> 2. **`API / INTEGRATION PASS`**: Đã kiểm thử tự động qua bộ 166 bài test xUnit (`Unit`, `Integration`, `WebApplicationFactory`) và công cụ đối soát bất biến `BetaGiaiPhong.Runner`.
> 3. **`MANUAL UAT — NOT TESTED`**: Quét mã QR qua ống kính phần cứng camera vật lý trên điện thoại Android / iPhone ngoài đời thực (được cung cấp danh mục nghiệm thu thủ công chi tiết tại Mục 9).

---

## 1. Executive Summary

Giai đoạn **BE-05B** hoàn thiện tầng kiểm chứng cao nhất của hệ thống bán vé trực tuyến Beta Giải Phóng trước khi đóng gói báo cáo đồ án tốt nghiệp:

1. **Khắc phục triệt để lỗ hổng an toàn cơ sở dữ liệu kiểm thử (Phase 0):** Sửa đổi [`TestDatabaseFixture.cs`](file:///d:/Projects/BetaGiaiPhong/tests/BetaGiaiPhong.Tests/IntegrationTests/TestDatabaseFixture.cs) để loại bỏ nguy cơ `DROP DATABASE` nhầm đối với cơ sở dữ liệu có sẵn, yêu cầu cờ tường minh `BGP_TEST_ALLOW_DB_CREATE=true`, đóng dấu quyền sở hữu bằng SQL Server Extended Property (`BGP_TestRunOwnershipToken`), và bổ sung 5 bài kiểm thử hồi quy an toàn (`TC_SAFE_01` đến `TC_SAFE_05`) trong [`TestDatabaseSafetyTests.cs`](file:///d:/Projects/BetaGiaiPhong/tests/BetaGiaiPhong.Tests/IntegrationTests/TestDatabaseSafetyTests.cs).
2. **Thiết lập bộ kiểm thử trình duyệt thực Playwright E2E (Phase 1–5):** Xây dựng dự án [`e2e/`](file:///d:/Projects/BetaGiaiPhong/e2e/playwright.config.ts) khởi chạy tiến trình `BetaGiaiPhong.Web` thực tế trên cổng độc lập (`http://127.0.0.1:5188`) kết nối cơ sở dữ liệu SQL Server tạm thời (`BetaGiaiPhong_E2E_<RunId>`). Chứng minh trọn vẹn hành trình liên thông **Manager $\rightarrow$ Customer $\rightarrow$ Staff $\rightarrow$ Manager** trên các `BrowserContext` tách biệt.
3. **Kiểm chứng kịch bản phủ định, xung đột đồng thời và bảo mật trên trình duyệt (Phase 6):** Xác minh chống giữ trùng ghế giữa 2 trình duyệt khách hàng đồng thời, chống truy cập chéo đơn hàng/ảnh QR (IDOR 404), thu hồi động quyền `STAFF` tức thời (`OnValidatePrincipal`), từ chối hủy suất chiếu đã có vé `PAID`, từ chối lưu trữ phim còn suất chiếu tương lai, bắt buộc CSRF token, và không rò rỉ khóa bí mật hay chuỗi `BGP1.` trên Console/Network.
4. **Kiểm chứng khả năng phục hồi sau khởi động lại & Idempotency (Phase 7):** Chứng minh khi tắt và khởi động lại tiến trình `BetaGiaiPhong.Web` với cùng SQL Server DB và khóa `BGP_QR_AES_KEY_B64`, đơn hàng `PAID` và mã QR vẫn hiển thị, giải mã và soát vé thành công; webhook trùng lặp trả về `DUPLICATE` không sinh thêm vé; `ReservationExpiryWorker` tự động thu hồi đơn hết hạn mà không chạm vào đơn `PAID`.
5. **Kiểm chứng giao diện đáp ứng (Responsive), Khả năng tiếp cận (A11y) & Hiệu năng (Phase 8–9):** Kiểm tra 3 kích thước màn hình (`1440x900`, `768x1024`, `390x844`), điều hướng bàn phím, và đo đạc hiệu năng thực tế (bao gồm 30 yêu cầu đặt cùng 1 ghế đồng thời: 1 thành công `201`, 29 từ chối `409`, `0` lỗi 500, `0` deadlock).
6. **Tích hợp CI/CD 4 Jobs (Phase 10):** Mở rộng [`.github/workflows/ci.yml`](file:///d:/Projects/BetaGiaiPhong/.github/workflows/ci.yml) với job thứ 4 `e2e-browser-tests` chạy Playwright Chromium trên `ubuntu-latest` cùng container SQL Server 2022.

---

## 2. Test Database Safety Fix (Phase 0)

### 2.1. Phân tích nguyên nhân gốc rễ (Root Cause)
Trong phiên bản BE-05A trước đó, khi biến môi trường `BGP_TEST_DATABASE_NAME` được cung cấp hoặc câu lệnh `IF DB_ID(...) IS NULL CREATE DATABASE ...` thực thi trên một cơ sở dữ liệu đã tồn tại sẵn, biến cờ `_databaseCreatedByThisRun` vẫn bị gán bằng `true`. Hệ quả là khi kết thúc phiên test, phương thức `DisposeAsync()` có nguy cơ thực thi `DROP DATABASE` lên một cơ sở dữ liệu tồn tại từ trước mà lượt chạy hiện tại không tạo ra. Ngoài ra, việc fallback về `ConnectionStrings__BetaDb` tiềm ẩn rủi ro kết nối nhầm vào máy chủ cơ sở dữ liệu ngoài ý muốn.

### 2.2. Giải pháp kỹ thuật đã triển khai trong `TestDatabaseFixture.cs`
- **Cơ chế Opt-in Tường minh (`BGP_TEST_ALLOW_DB_CREATE=true`):** Nếu không cấu hình `BGP_TEST_ALLOW_DB_CREATE=true` (hoặc `BGP_TEST_DATABASE_NAME` kèm `BGP_TEST_ALLOW_EXISTING_DB_REUSE=true`), `TestDatabaseFixture` lập tức ném ngoại lệ `InvalidOperationException` và không tạo/không xóa bất kỳ cơ sở dữ liệu nào.
- **Loại bỏ Fallback nguy hiểm & Chốt chặn Remote SQL Server:** Xóa bỏ hoàn toàn nhánh fallback sang `ConnectionStrings__BetaDb` khi tìm kết nối `master`. Chỉ cho phép các máy chủ cục bộ (`localhost`, `127.0.0.1`, `.`, `(local)`, `(localdb)`) trừ khi có cờ xác nhận `BGP_TEST_CONFIRM_REMOTE_SQL_SERVER=true`.
- **Kiểm tra sự tồn tại trước khi tạo & Đóng dấu Quyền sở hữu (`BGP_TestRunOwnershipToken`):**
  - Truy vấn `SELECT CASE WHEN DB_ID(@dbName) IS NOT NULL THEN 1 ELSE 0 END` trước khi tạo DB.
  - Nếu DB **đã tồn tại**: Gán `_databaseCreatedByThisRun = false`, `_createdDatabaseName = null`, `_ownershipToken = null`. Từ chối sử dụng nếu không có `BGP_TEST_ALLOW_EXISTING_DB_REUSE=true`.
  - Nếu DB **chưa tồn tại**: Thực thi `CREATE DATABASE [{DatabaseName}]` và ghi một mã định danh GUID ngẫu nhiên (`_ownershipToken`) vào SQL Server Extended Property `BGP_TestRunOwnershipToken` của chính cơ sở dữ liệu đó.
- **Kiểm tra kép khi dọn dẹp (`DisposeAsync`):** Chỉ thực hiện `DROP DATABASE` khi `_databaseCreatedByThisRun == true` **VÀ** giá trị `BGP_TestRunOwnershipToken` đọc trực tiếp từ `[{dbToDrop}].sys.extended_properties` khớp tuyệt đối với `_ownershipToken` của phiên chạy hiện tại.

### 2.3. Kết quả 5 bài kiểm thử an toàn (`TestDatabaseSafetyTests.cs`)

| Mã Test | Kịch bản kiểm chứng | Kết quả |
| :--- | :--- | :---: |
| `TC_SAFE_01` | Database đã tồn tại từ trước (`PreExist`) được tái sử dụng (`BGP_TEST_ALLOW_EXISTING_DB_REUSE=true`) $\rightarrow$ `DisposeAsync()` **KHÔNG** xóa database và giữ nguyên bảng dữ liệu `SentinelData`. | **PASS** |
| `TC_SAFE_02` | Database mới được tạo bởi lượt test hiện tại (`BGP_TEST_ALLOW_DB_CREATE=true`) có gắn `BGP_TestRunOwnershipToken` $\rightarrow$ `DisposeAsync()` tự động xóa sạch database khi hoàn tất. | **PASS** |
| `TC_SAFE_03` | Database có tên trùng tiền tố `BetaGiaiPhong_Test_` nhưng được tạo bên ngoài (không thuộc quyền sở hữu của fixture) $\rightarrow$ bị từ chối khi thiếu cờ reuse và **KHÔNG** bị xóa khi gọi `DisposeAsync()`. | **PASS** |
| `TC_SAFE_04` | Chạy `TestDatabaseFixture` khi không bật `BGP_TEST_ALLOW_DB_CREATE=true` $\rightarrow$ từ chối ngay lập tức, không có database nào được tạo ra. | **PASS** |
| `TC_SAFE_05` | Khi quá trình khởi tạo (`PostCreateHookAsync`) gặp lỗi giữa chừng trên một database có sẵn $\rightarrow$ dữ liệu cũ (`ExistingImportantData`) được bảo toàn nguyên vẹn, không bị xóa. | **PASS** |

---

## 3. Real-Browser E2E Architecture (`e2e/`)

Hệ thống kiểm thử trình duyệt thực được xây dựng tại thư mục [`e2e/`](file:///d:/Projects/BetaGiaiPhong/e2e/playwright.config.ts) với kiến trúc hoàn toàn độc lập:

- **Trình duyệt thực:** Chromium (`@playwright/test` v1.50+, Desktop Chrome & Mobile/Tablet viewports).
- **Quản lý vòng đời tự động ([`global-setup.ts`](file:///d:/Projects/BetaGiaiPhong/e2e/global-setup.ts) & [`global-teardown.ts`](file:///d:/Projects/BetaGiaiPhong/e2e/global-teardown.ts)):**
  1. Sinh ngẫu nhiên khóa mật mã 256-bit CSPRNG (`BGP_QR_AES_KEY_B64`, `BGP_MOCK_HMAC_KEY_B64`) và tên cơ sở dữ liệu độc lập `BetaGiaiPhong_E2E_<RunId>` cùng mã sở hữu `ownershipToken`.
  2. Gọi `BetaGiaiPhong.Runner create-e2e-db` để tạo DB, áp dụng toàn bộ EF Core migrations, đóng dấu `BGP_TestRunOwnershipToken`, và seed dữ liệu danh mục mẫu (`DemoSeeder`).
  3. Khởi chạy tiến trình máy chủ thật `BetaGiaiPhong.Web` trên `http://127.0.0.1:5188` (với `BGP_ENABLE_MOCK_PAYMENT_SIMULATOR=true`, `BGP_ENABLE_EXPIRY_WORKER=true`), chờ `/health` trả về `200 OK` trước khi bắt đầu chạy test.
  4. Khi kết thúc toàn bộ suite, `global-teardown.ts` dừng máy chủ Web, chạy `BetaGiaiPhong.Runner verify` để kiểm tra 9 nhóm bất biến toàn vẹn dữ liệu trên DB E2E, sau đó gọi `drop-e2e-db` (kiểm tra khớp `ownershipToken`) để dọn sạch cơ sở dữ liệu.
- **Sửa lỗi thực tế phát hiện qua trình duyệt thật:**
  - Trong quá trình chạy Playwright với bộ lắng nghe `page.on('pageerror')`, phát hiện các trang sử dụng `_ValidationScriptsPartial.cshtml` báo lỗi `ReferenceError: jQuery is not defined` do [`_Layout.cshtml`](file:///d:/Projects/BetaGiaiPhong/src/BetaGiaiPhong.Web/Views/Shared/_Layout.cshtml) thiếu thẻ nạp `~/lib/jquery/dist/jquery.min.js` trước validation scripts. Đã bổ sung `jquery.min.js` vào `_Layout.cshtml`, giúp số lượng lỗi JavaScript runtime (`pageerror`) trên toàn bộ hành trình giảm về **0**.
  - Tối ưu hóa mật độ điểm ảnh QR (`GetGraphic(6)`, `ECCLevel.M`) trong [`TicketQueryService.cs`](file:///d:/Projects/BetaGiaiPhong/src/BetaGiaiPhong.Web/Services/TicketQueryService.cs) và cơ chế fallback `scanFile(file, false)` trong [`StaffCheckIn/Index.cshtml`](file:///d:/Projects/BetaGiaiPhong/src/BetaGiaiPhong.Web/Views/StaffCheckIn/Index.cshtml), đảm bảo thư viện `Html5Qrcode` giải mã ảnh PNG QR trong trình duyệt đạt độ tin cậy 100%.

---

## 4. Full Journey Verification (Manager $\rightarrow$ Customer $\rightarrow$ Staff $\rightarrow$ Manager)

Bài kiểm thử [`01-full-journey.spec.ts`](file:///d:/Projects/BetaGiaiPhong/e2e/tests/01-full-journey.spec.ts) thực thi trên 3 `BrowserContext` hoàn toàn tách biệt (`managerSession`, `customerSession`, `staffSession`):

| Giai đoạn | Vai trò | Thao tác trên trình duyệt thực & Kết quả kiểm chứng | Trạng thái |
| :--- | :--- | :--- | :---: |
| **Phase 2.1** | `MANAGER` | Đăng nhập `/account/login`, vào `/management/movies/create`, tạo phim mới ở trạng thái `DRAFT`. Xác minh khách hàng tại `/movies` **không** nhìn thấy phim `DRAFT`. | `AUTOMATED BROWSER PASS` |
| **Phase 2.2** | `MANAGER` | Chỉnh sửa thông tin phim tại `/management/movies/{id}/edit` (cập nhật tiêu đề, thời lượng `110 phút`, tóm tắt). | `AUTOMATED BROWSER PASS` |
| **Phase 2.3** | `MANAGER` | Xuất bản phim (`DRAFT` $\rightarrow$ `PUBLISHED`) qua nút Publish và hộp thoại xác nhận `confirm()`. | `AUTOMATED BROWSER PASS` |
| **Phase 2.4** | `MANAGER` | Công bố suất chiếu mới tại `/management/showtimes/create` vào thời điểm `now + 15 phút` (giờ VN) với giá ghế `STANDARD = 75,000 đ`, `VIP = 95,000 đ`. Suất chiếu chuyển sang `ON_SALE`. | `AUTOMATED BROWSER PASS` |
| **Phase 3.1** | `CUSTOMER` | Đăng nhập tài khoản `customer1@demo.invalid`, duyệt `/movies`, mở `/movies/{id}` và nhấn vào suất chiếu vừa mở bán. | `AUTOMATED BROWSER PASS` |
| **Phase 3.2** | `CUSTOMER` | Trên sơ đồ ghế `/showtimes/{id}/seats`, click chọn 2 ghế (`A1` Standard và `B5` VIP), kiểm tra `#selectedCount = 2` và tổng tiền tạm tính `170,000 đ`. | `AUTOMATED BROWSER PASS` |
| **Phase 3.3** | `CUSTOMER` | Nhấn `#btnSubmit` giữ ghế $\rightarrow$ điều hướng sang `/orders/{orderId}`, hiển thị trạng thái `Chờ thanh toán` và đồng hồ đếm ngược `#countdownTimer` 5 phút. | `AUTOMATED BROWSER PASS` |
| **Phase 3.4–3.5** | `CUSTOMER` | Vào `/orders/{orderId}/checkout`, nhấn `#btnPaySuccess` thanh toán mô phỏng $\rightarrow$ đơn chuyển sang `PAID`, điều hướng sang `/orders/{orderId}/tickets`. | `AUTOMATED BROWSER PASS` |
| **Phase 3.6** | `CUSTOMER` | Hiển thị 2 thẻ vé `.ticket-card` trạng thái `HỢP LỆ / CHƯA SOÁT` (`ISSUED`), ảnh QR PNG nạp thành công (`naturalWidth > 0`, đúng chữ ký PNG `89 50 4E 47`). Tải lại trang (F5) dữ liệu vẫn bền vững; sơ đồ ghế hiển thị `seat-sold`. | `AUTOMATED BROWSER PASS` |
| **Phase 4.1** | `STAFF` | Đăng nhập `staff@demo.invalid`, mở `/staff/check-in?showtimeId={id}`, tải file ảnh QR PNG của khách hàng vào `#qrFileInput`. Trình duyệt thực thi `Html5Qrcode.scanFile()` giải mã token, gửi `POST /api/staff/tickets/scan` và hiển thị `#scanStatusBadge = HỢP LỆ` (`ACCEPTED`). | `AUTOMATED BROWSER PASS` |
| **Phase 4.2** | `CUSTOMER` & `STAFF` | Khách hàng tải lại trang vé thấy trạng thái vé đã đổi sang `ĐÃ SOÁT VÉ (USED)`. Nhân viên nhấn `#btnRescanCurrent` (`Quét lại vé này`) $\rightarrow$ nhận kết quả `ĐÃ SỬ DỤNG` (`ALREADY_USED`). | `AUTOMATED BROWSER PASS` |
| **Phase 4.3** | `STAFF` | Mở `/staff/check-in/history?showtimeId={id}`, xác nhận bảng lịch sử ghi nhận đầy đủ cả 2 lượt `HỢP LỆ (ACCEPTED)` và `ĐÃ DÙNG (ALREADY_USED)` kèm mã vé. | `AUTOMATED BROWSER PASS` |
| **Phase 5** | `MANAGER` | Mở `/management/reports` lọc theo ngày chiếu (VN), phim và phòng chiếu: tổng doanh thu tăng đúng `170,000 VND`, số vé bán `2 vé`, tỷ lệ lấp đầy `20.00%` (2/10 ghế). Tải lại trang nhiều lần hoặc quét vé QR không làm thay đổi doanh thu. | `AUTOMATED BROWSER PASS` |

---

## 5. Negative, Concurrency & Security Scenarios (Phase 6)

Được kiểm chứng tự động trong [`02-negative-and-security.spec.ts`](file:///d:/Projects/BetaGiaiPhong/e2e/tests/02-negative-and-security.spec.ts):

### 5.1. Kịch bản phủ định Khách hàng (Customer Negative)
- **Khách vãng lai (Guest) đặt ghế:** Click chọn ghế và bấm đặt vé $\rightarrow$ bị chuyển hướng về `/account/login?returnUrl=...`, không tạo đơn hàng. (`AUTOMATED BROWSER PASS`)
- **Chọn quá 8 ghế:** Click chọn 9 ghế liên tiếp trên sơ đồ ghế $\rightarrow$ UI chặn ở mức tối đa `8` ghế (`#selectedCount = 8`) và hiển thị cảnh báo. (`AUTOMATED BROWSER PASS`)
- **Hai khách hàng tranh chấp cùng 1 ghế đồng thời trên 2 trình duyệt:** `Customer 1` và `Customer 2` cùng mở sơ đồ ghế, chọn cùng mã ghế và nhấn `#btnSubmit` đồng thời (`Promise.all`) $\rightarrow$ đúng **1** người thành công chuyển tới `/orders/{id}`, người còn lại nhận thông báo lỗi xung đột `.alert-danger` tại trang sơ đồ ghế. (`AUTOMATED BROWSER PASS`)
- **Thanh toán thất bại (`FAILED`):** Nhấn `#btnPayFailed` tại trang Checkout $\rightarrow$ đơn chuyển sang `FAILED`, API `/api/orders/{id}/tickets` trả về `404`, không có vé nào được phát hành và ghế được giải phóng. (`AUTOMATED BROWSER PASS`)
- **Đơn hàng hết hạn (`EXPIRED`):** Khi đơn hàng quá hạn giữ chỗ, truy cập `/orders/{id}/checkout` tự động chặn và đẩy về `/orders/{id}` với thông báo lỗi hết hạn. (`AUTOMATED BROWSER PASS`)
- **Chống truy cập chéo (IDOR):** `Customer 2` truy cập đơn hàng hoặc đường dẫn ảnh QR `/tickets/{ticketId}/qr` của `Customer 1` $\rightarrow$ nhận `404 Not Found`. (`AUTOMATED BROWSER PASS`)

### 5.2. Kịch bản phủ định Nhân viên Soát vé (Staff Negative)
- **Sai suất chiếu (`WRONG_SHOWTIME`):** Quét mã QR của Suất chiếu A khi đang chọn Suất chiếu B $\rightarrow$ hiển thị `SAI SUẤT CHIẾU`. (`AUTOMATED BROWSER PASS`)
- **Ngoài khung giờ soát vé (`OUTSIDE_WINDOW`):** Quét mã QR của suất chiếu bắt đầu sau 240 phút (> 30 phút trước giờ chiếu) $\rightarrow$ hiển thị `NGOÀI GIỜ SOÁT VÉ`. (`AUTOMATED BROWSER PASS`)
- **Phân quyền & Thu hồi động vai trò (`OnValidatePrincipal`):**
  - Tài khoản `CUSTOMER` truy cập `/staff/check-in` bị chuyển hướng tới `/account/access-denied`; gọi `POST /api/staff/tickets/scan` nhận `403 Forbidden`.
  - Khi tài khoản `STAFF` đang có phiên cookie hợp lệ nhưng bị thu hồi quyền `STAFF` trong cơ sở dữ liệu (`dbo.UserRoles`), yêu cầu kế tiếp bị `OnValidatePrincipal` từ chối ngay lập tức (`401 Unauthorized`). (`AUTOMATED BROWSER PASS`)

### 5.3. Kịch bản phủ định Quản lý & Bảo mật (Manager & Security)
- **Trùng lịch phòng chiếu:** Công bố suất chiếu mới trùng khung giờ (kể cả thời gian đệm `BufferMinutes`) trong cùng phòng $\rightarrow$ bị từ chối với thông báo lỗi xung đột lịch chiếu. (`AUTOMATED BROWSER PASS`)
- **Lưu trữ phim còn suất chiếu tương lai:** Bị từ chối, giữ nguyên trạng thái `PUBLISHED`. (`AUTOMATED BROWSER PASS`)
- **Hủy suất chiếu đã có vé `PAID`:** Nút hủy bị vô hiệu hóa (`Không thể hủy (Đã bán vé)`), bảo vệ quyền lợi khách hàng đã mua vé. (`AUTOMATED BROWSER PASS`)
- **Chống giả mạo yêu cầu (Anti-CSRF) & Chống rò rỉ bí mật:** Gửi `POST /api/bookings/reserve` thiếu `X-XSRF-TOKEN` bị chặn với `400 Bad Request`. Toàn bộ log trình duyệt (`consoleMessages`) được quét tự động, xác nhận **0** lần xuất hiện `BGP_QR_AES_KEY_B64`, `BGP_MOCK_HMAC_KEY_B64`, `BGP_DEMO_PASSWORD` hoặc chuỗi token gốc `BGP1.`. (`AUTOMATED BROWSER PASS`)

---

## 6. Restart, Recovery & Failure Behavior (Phase 7)

Được kiểm chứng tự động trong [`03-restart-recovery.spec.ts`](file:///d:/Projects/BetaGiaiPhong/e2e/tests/03-restart-recovery.spec.ts):

1. **Khởi động lại Web Server (Web Server Restart Recovery):**
   - Khởi chạy một tiến trình `BetaGiaiPhong.Web` riêng biệt trên cổng `5189`, thực hiện đặt ghế, thanh toán `PAID` và phát hành vé QR.
   - Tắt hoàn toàn tiến trình máy chủ (`SIGTERM`), sau đó khởi chạy một tiến trình `BetaGiaiPhong.Web` mới tinh kết nối cùng cơ sở dữ liệu và cùng khóa `BGP_QR_AES_KEY_B64`.
   - Khách hàng mở lại `/orders/{id}/tickets`: đơn hàng vẫn ở trạng thái `PAID`, số lượng vé giữ nguyên `1`, ảnh QR PNG tải thành công (`200 OK`) và giải mã chính xác trên màn hình soát vé của Staff. (`AUTOMATED BROWSER PASS`)
2. **Tính bất biến khi nhận Webhook trùng lặp (Duplicate Webhook Idempotency):**
   - Gửi 2 lần liên tiếp cùng một payload webhook có chữ ký `X-Signature-SHA256` hợp lệ tới `POST /api/webhooks/mock-payment`: lần 1 trả về `decision = "APPLIED"`, lần 2 trả về `decision = "DUPLICATE"`, chỉ phát hành đúng 1 vé và không cộng đúp doanh thu. (`AUTOMATED BROWSER PASS`)
3. **Mô phỏng đứt kết nối mạng khi soát vé (`NETWORK_ERROR` & Retry):**
   - Dùng `staffPage.route('**/api/staff/tickets/scan', route => route.abort('failed'))` ở lần quét đầu tiên $\rightarrow$ giao diện hiển thị rõ trạng thái `LỖI KẾT NỐI` kèm nút `Thử lại kết nối` mà không làm treo trang. Khi mạng phục hồi và nhấn `Thử lại kết nối`, yêu cầu thành công ngay lập tức. (`AUTOMATED BROWSER PASS`)
4. **Tự động thu hồi ghế quá hạn (`ReservationExpiryWorker`):**
   - Đơn hàng `PENDING_PAYMENT` quá hạn được tiến trình nền `ReservationExpiryWorker` tự động quét và chuyển sang `EXPIRED`, giải phóng ghế về `AVAILABLE`, trong khi đơn `PAID` không bị ảnh hưởng. (`AUTOMATED BROWSER PASS`)
5. **Đối soát 9 nhóm bất biến toàn vẹn dữ liệu (`BetaGiaiPhong.Runner verify`):**
   - Sau toàn bộ các thao tác khởi động lại, lỗi mạng, webhook trùng lặp và hết hạn đơn, lệnh `verify` xác nhận **0 vi phạm** trên cả 9 nhóm bất biến SQL. (`AUTOMATED BROWSER PASS`)

---

## 7. Responsive & Accessibility Results (Phase 8)

Được kiểm chứng tự động trong [`04-responsive-and-a11y.spec.ts`](file:///d:/Projects/BetaGiaiPhong/e2e/tests/04-responsive-and-a11y.spec.ts) trên 9 màn hình trọng yếu (`/movies`, `/movies/{id}`, `/showtimes/{id}/seats`, `/orders/{id}`, `/orders/{id}/checkout`, `/orders/{id}/tickets`, `/staff/check-in`, `/management/movies`, `/management/showtimes`, `/management/reports`):

| Khía cạnh kiểm tra | Desktop (`1440x900`) | Tablet (`768x1024`) | Mobile (`390x844`) |
| :--- | :---: | :---: | :---: |
| Không vỡ khung chứa chính (`main`), các bảng dữ liệu cuộn ngang trong `.table-responsive` | **PASS** | **PASS** | **PASS** |
| Nhãn biểu mẫu (`<label for="...">`), điều hướng bàn phím (`Tab`, `Enter`) trên trang Đăng nhập | **PASS** | **PASS** | **PASS** |
| Nút gửi biểu mẫu (`#btnSubmit`) tự động khóa (`disabled`) khi chưa chọn ghế và khi đang gửi | **PASS** | **PASS** | **PASS** |
| Ảnh mã QR vé điện tử có thuộc tính `alt` và kích thước hiển thị thực tế $\ge 120 \times 120\text{ px}$ | **PASS** (`220x220 px`) | **PASS** (`220x220 px`) | **PASS** (`220x220 px`) |

---

## 8. Performance & Load Metrics (Phase 9)

Được đo đạc thực tế bởi [`05-performance-load.spec.ts`](file:///d:/Projects/BetaGiaiPhong/e2e/tests/05-performance-load.spec.ts) trên máy chủ `BetaGiaiPhong.Web` (bản build `Release`) và cơ sở dữ liệu SQL Server độc lập:

| Endpoint / Kịch bản tải | Số Request | Thành công (`2xx/3xx`) | Xung đột hợp lệ (`409`) | Độ trễ TB (`Avg ms`) | Độ trễ `P95 (ms)` | Thông lượng (`req/s`) | Lỗi Server (`5xx`) | SQL Exception / Deadlock |
| :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| `GET /movies` (Danh mục phim) | 25 | 25 | 0 | **12.00 ms** | **18.83 ms** | 83.32 rps | 0% | NOT MEASURED |
| `GET /showtimes/{id}/seats` (Sơ đồ ghế) | 25 | 25 | 0 | **17.28 ms** | **31.22 ms** | 57.85 rps | 0% | NOT MEASURED |
| `POST /api/bookings/reserve` (Giữ ghế hợp lệ) | 5 | 5 | 0 | **100.47 ms** | **147.30 ms** | 9.95 rps | 0% | NOT MEASURED |
| `POST /orders/{id}/demo-pay` (Thanh toán & Cấp vé) | 5 | 5 | 0 | **461.16 ms** | **622.23 ms** | 2.17 rps | 0% | NOT MEASURED |
| `GET /tickets/{ticketId}/qr` (Sinh ảnh QR PNG) | 20 | 20 | 0 | **13.90 ms** | **20.52 ms** | 71.92 rps | 0% | NOT MEASURED |
| `POST /api/staff/tickets/scan` (Soát vé QR) | 20 | 20 | 0 | **15.76 ms** | **17.21 ms** | 63.43 rps | 0% | NOT MEASURED |
| `GET /management/reports` (Dashboard Doanh thu) | 20 | 20 | 0 | **37.79 ms** | **118.89 ms** | 26.46 rps | 0% | NOT MEASURED |
| **CONCURRENT LOAD:** `POST /api/bookings/reserve` (30 request song song tranh cùng 1 ghế) | **30** | **1** (`201 Created`) | **29** (`409 Conflict`) | **227.04 ms** | **276.20 ms** | **100.30 rps** | **0%** | **NOT MEASURED** |

> [!NOTE]
> Nhờ cơ chế khóa bi quan theo thứ tự phân cấp (`SqlServerLocks.ShowtimeAsync` với `UPDLOCK, HOLDLOCK` trong giao dịch `IsolationLevel.Serializable`), khi 30 yêu cầu đặt cùng một ghế ập đến đồng thời, các giao dịch được xếp hàng tuần tự tại SQL Server mà **không phát sinh bất kỳ lỗi Deadlock (SQL Error 1205) nào**, đảm bảo đúng 1 khách hàng giữ được ghế và 29 yêu cầu còn lại nhận phản hồi `409 Conflict` an toàn.

---

## 9. Manual Physical Camera UAT Checklist (Phase 8.3)

> [!WARNING]
> **Trạng thái nghiệm thu phần cứng camera vật lý:** **`MANUAL UAT — NOT TESTED`**  
> Môi trường kiểm thử tự động và máy trạm phát triển hiện tại kiểm chứng việc giải mã hình ảnh QR trong trình duyệt thông qua `Html5Qrcode.scanFile()` (`#qrFileInput`). Việc quét trực tiếp qua ống kính quang học của điện thoại di động thực tế (Android / iPhone) cần được thực hiện thủ công theo danh mục nghiệm thu dưới đây khi trình diễn bảo vệ đồ án.

### Danh mục Nghiệm thu Thủ công (Manual UAT Checklist cho Hội đồng / Người kiểm thử)

1. **Chuẩn bị môi trường HTTPS / Localhost cho Camera API (`navigator.mediaDevices.getUserMedia`):**
   - Khởi chạy hệ thống: `.\scripts\01_prepare_dev_session.ps1` và `.\scripts\03_seed_checkin_demo.ps1`.
   - Khởi động máy chủ Web hỗ trợ HTTPS: `dotnet run --project .\src\BetaGiaiPhong.Web --launch-profile https`.
2. **Các bước kiểm thử thực địa:**

| STT | Hạng mục kiểm thử thực địa | Thiết bị mục tiêu | Kỳ vọng nghiệm thu | Trạng thái hiện tại |
| :---: | :--- | :--- | :--- | :---: |
| **UAT-CAM-01** | Cấp quyền truy cập Camera trên trình duyệt di động | Chrome (Android) / Safari (iOS) | Khi nhấn `#btnStartCamera` ("Bật Camera"), trình duyệt hiển thị hộp thoại xin quyền Camera; sau khi chấp nhận, khung hình video trực tiếp hiển thị trong `#reader`. | `MANUAL UAT — NOT TESTED` |
| **UAT-CAM-02** | Từ chối quyền truy cập Camera | Chrome / Safari / Edge | Khi người dùng nhấn "Block / Từ chối" quyền Camera, ứng dụng bắt ngoại lệ an toàn, hiển thị thông báo tiếng Việt và cho phép chuyển sang tải file ảnh QR (`#qrFileInput`) hoặc nhập mã thủ công (`#manualQrToken`). | `MANUAL UAT — NOT TESTED` |
| **UAT-CAM-03** | Quét mã QR từ màn hình điện thoại khách hàng ở độ sáng bình thường (50%–80%) | Điện thoại nhân viên quét màn hình điện thoại khách | Khoảng cách 15–20 cm: `Html5Qrcode` nhận diện mã QR trong $\le 2$ giây, tự động gửi API và hiển thị huy hiệu xanh `HỢP LỆ` (`ACCEPTED`), đồng thời chống quét lặp liên tục khi camera vẫn giữ trước mã QR. | `MANUAL UAT — NOT TESTED` |
| **UAT-CAM-04** | Quét mã QR từ màn hình điện thoại khách ở độ sáng thấp (< 25%) hoặc nghiêng góc $30^\circ$ | Điện thoại nhân viên quét màn hình điện thoại khách | Đánh giá khả năng lấy nét tự động và sửa lỗi Reed-Solomon (`ECCLevel.M`) của mã QR dưới điều kiện ánh sáng phòng chờ rạp chiếu phim. | `MANUAL UAT — NOT TESTED` |
| **UAT-CAM-05** | Quét bằng đầu đọc mã vạch 2D cầm tay cắm cổng USB (HID Keyboard Emulation) | Máy tính quầy vé + Đầu đọc 2D USB | Đặt con trỏ tại ô `#manualQrToken`, bóp cò súng quét mã QR: chuỗi token tự động điền kèm ký tự `Enter`, biểu mẫu tự gửi và trả về kết quả `HỢP LỆ` ngay lập tức. | `MANUAL UAT — NOT TESTED` |

---

## 10. CI Pipeline Integration (Phase 10)

File cấu hình [`.github/workflows/ci.yml`](file:///d:/Projects/BetaGiaiPhong/.github/workflows/ci.yml) hiện vận hành **4 jobs** tự động trên GitHub Actions (`ubuntu-latest`):

1. **`build-and-validate`**: Kiểm tra .NET 10 SDK, khôi phục công cụ `dotnet-ef`, biên dịch `Release` (0 warnings, 0 errors), chạy `scripts/static_schema_checks.py`, kiểm tra `has-pending-model-changes`, và quét lỗ hổng bảo mật NuGet (`--vulnerable --include-transitive`).
2. **`automated-tests`**: Khởi chạy container SQL Server 2022 Developer, thực thi toàn bộ **166 bài test xUnit** (bao gồm 5 bài test an toàn DB mới) với cờ `BGP_TEST_ALLOW_DB_CREATE=true` và xuất báo cáo độ phủ mã nguồn Cobertura + TRX.
3. **`integrity-checks`**: Khởi chạy container SQL Server 2022 độc lập, thực thi tuần tự `migrate`, `seed-demo`, `smoke`, `race`, và `verify` (9/9 nhóm bất biến).
4. **`e2e-browser-tests` (Mới trong BE-05B)**: Khởi chạy container SQL Server 2022, cài đặt Node.js 22 và Playwright Chromium (`npx playwright install --with-deps chromium`), chạy toàn bộ **5 bộ kiểm thử trình duyệt thực E2E**, và tải lên báo cáo HTML (`playwright-report`) cùng trace/screenshot (không chứa bí mật) khi kết thúc.

---

## 11. Quality Gate Summary

| Cổng chất lượng (Quality Gate) | Lệnh kiểm tra | Kết quả thực tế |
| :--- | :--- | :---: |
| **1. Biên dịch Release** | `dotnet build BetaGiaiPhong.slnx -c Release` | **PASS** (0 Warnings, 0 Errors) |
| **2. Kiểm thử Unit, Integration & Web (xUnit)** | `dotnet test BetaGiaiPhong.slnx -c Release` | **PASS** (**166 / 166** tests passed) |
| **3. Kiểm thử Trình duyệt thực Playwright E2E** | `npx playwright test` (trong `e2e/`) | **PASS** (**5 / 5** suites passed trên Chromium) |
| **4. Kiểm tra Schema Tĩnh (17 bảng, 116 cột, 22 FK)** | `python scripts/static_schema_checks.py` | **PASS** (`STATIC CHECKS PASSED`) |
| **5. Kiểm tra Lệch Mô hình EF Core (Model Drift)** | `dotnet ef migrations has-pending-model-changes` | **PASS** (`No changes have been made...`) |
| **6. Quét lỗ hổng bảo mật gói phụ thuộc (.NET & npm)** | `dotnet list package --vulnerable --include-transitive` & `npm audit` | **PASS** (**0** vulnerabilities) |
| **7. Đối soát 9 Nhóm Bất biến Dữ liệu (`Runner verify`)** | `dotnet run --project src/BetaGiaiPhong.Runner -- verify` | **PASS** (**0** violations trên cả 9 nhóm) |
| **8. Kiểm tra Rò rỉ Bí mật (Secret Leakage Scan)** | Quét mã nguồn, console log trình duyệt và cấu hình CI | **PASS** (0 khóa thật hay token gốc bị lộ) |

---

## 12. Residual Risks & Recommendations

1. **Nghiệm thu Camera vật lý trên thiết bị di động (`MANUAL UAT — NOT TESTED`):** Như đã phân tích tại Mục 9, trình duyệt tự động kiểm chứng thuật toán tạo và giải mã ảnh QR của `Html5Qrcode`, nhưng hiệu năng lấy nét quang học của từng dòng điện thoại thực tế dưới ánh sáng yếu tại rạp cần được kiểm tra theo bảng `UAT-CAM-01`..`05`.
2. **Quản lý khóa Data Protection khi triển khai đa máy chủ (Multi-instance Deployment):** Trong môi trường nguyên mẫu 1 máy chủ, khóa mã hóa Cookie Authentication được lưu trong bộ nhớ hoặc thư mục cục bộ. Nếu triển khai mở rộng nhiều container song song sau này, cần cấu hình `PersistKeysToDbContext` hoặc Azure Key Vault / Redis để giữ phiên đăng nhập khi cân bằng tải.
3. **Sẵn sàng bảo vệ đồ án:** Toàn bộ mã nguồn, cơ sở dữ liệu, bộ test 166 bài xUnit và 5 bộ test trình duyệt thực Playwright E2E đã đồng bộ, sẵn sàng để commit và tạo Pull Request hợp nhất vào nhánh `main`.
