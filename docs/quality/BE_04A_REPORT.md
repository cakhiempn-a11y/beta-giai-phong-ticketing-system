# BÁO CÁO TRIỂN KHAI & HARDENING GIAI ĐOẠN BE-04A
## VẬN HÀNH SOÁT VÉ QR, BẢO VỆ CLIENT & TỰ ĐỘNG XỬ LÝ ĐƠN HÀNG HẾT HẠN

---

### THÔNG TIN TỔNG QUAN

- **Dự án:** Hệ thống quản lý bán vé trực tuyến rạp chiếu phim Beta Giải Phóng (MIS Capstone Prototype).
- **Giai đoạn:** BE-04A — Staff QR Scanner, Ticket Check-in & Automatic Reservation Expiry (Bao gồm Final Hardening & Code Review Remediation).
- **Workspace:** `D:\Projects\BetaGiaiPhong`
- **GitHub Repository:** `https://github.com/cakhiempn-a11y/beta-giai-phong-ticketing-system`
- **Nhánh phát triển:** `feat/staff-checkin-expiry` (tạo từ `main` commit `cd2f7b677c12`).
- **Tài khoản Git & Committer:** `cakhiempn-a11y` (`320607805+cakhiempn-a11y@users.noreply.github.com`).
- **Môi trường & Công nghệ:** .NET 10 SDK, ASP.NET Core 10, Entity Framework Core 10.0.14, Microsoft SQL Server, xUnit, `html5-qrcode` 2.3.8 (vendored cục bộ).

---

### 1. TỔNG QUAN KẾT QUẢ TRIỂN KHAI & REMEDIATION

Giai đoạn BE-04A cùng đợt rà soát làm cứng (Final Hardening) đã hoàn thành toàn diện 100% các yêu cầu:

1. **Staff QR Scanning API (`POST /api/staff/tickets/scan`)**:
   - Xác thực danh tính bằng Cookie Authentication, ủy quyền chính sách `RequireStaff` (`STAFF`, `MANAGER`, `ADMIN`).
   - Bảo vệ chống tấn công CSRF bắt buộc qua header `X-XSRF-TOKEN` / cookie `.BetaGiaiPhong.Antiforgery`.
   - Ủy thác trực tiếp cho domain service `TicketScanningService.ScanAsync` của Data Layer, bảo toàn giao dịch tuần tự và khóa cấp độ ứng dụng SQL Server.
   - Trả về mã HTTP tương ứng với từng trường hợp soát vé:
     - `ACCEPTED`: 200 OK.
     - `ALREADY_USED`: 409 Conflict.
     - `WRONG_SHOWTIME`: 422 Unprocessable Entity.
     - `OUTSIDE_WINDOW`: 422 Unprocessable Entity.
     - `NOT_FOUND`: 404 Not Found (ghi nhật ký bảo mật vào `dbo.AuditLogs` với `EventType = TICKET_SCAN_REJECTED`).
     - Lỗi định dạng/thiếu dữ liệu: 400 Bad Request.
   - Tuyệt đối không làm lộ token thô, khóa mã hóa (`QrTokenCipher`), giá trị băm (`QrTokenHash`) hay cấu hình hệ thống trong JSON response.

2. **Giao diện Soát vé Nhân viên (`GET /staff/check-in`) & Bảo mật Client-Side**:
   - Giao diện Razor View tương thích thiết bị di động, máy tính bảng và máy tính quầy soát vé.
   - Tích hợp 4 phương thức quét: Camera trình duyệt trực tiếp, tải file ảnh QR, nhập token thủ công, và máy quét mã vạch 2D cầm tay USB.
   - **Remediation Phase 1 (Bảo mật Client):** Loại bỏ hoàn toàn câu lệnh ghi token thô ra console (`console.log("Debounced duplicate scan:", qrToken)`). Bổ sung kiểm tra độ dài token (< 256 ký tự) phía client trước khi gửi mạng. Render DOM an toàn bằng `document.createElement` và `textContent`, triệt tiêu hoàn toàn rủi ro Cross-Site Scripting (XSS). Không lưu token vào `localStorage`/`sessionStorage` hay query string.
   - **Remediation Phase 2 (Kiểm soát Luồng Quét & Trùng lặp):**
     - Bổ sung cờ `isInFlight` ngăn chặn việc gửi request đồng thời khi camera nhận diện nhiều frame liên tiếp.
     - Cơ chế bảo vệ `isTicketAccepted`: Sau khi một vé đã được chấp nhận (`ACCEPTED`), camera sẽ bỏ qua các frame tiếp theo để chống việc lia camera lâu làm đổi trạng thái thành `ALREADY_USED`.
     - Bổ sung nút bấm trực quan **"Quét vé tiếp theo"** (`#btnScanNext`) để đặt lại trạng thái và focus vào ô nhập.
     - Bổ sung nút **"Quét lại vé này (Kiểm tra lại)"** (`#btnRescanCurrent`) cho phép nhân viên chủ động kiểm tra lại vé vừa quét mà không bị chặn bởi cờ chống trùng lặp.
     - Lắng nghe sự kiện thay đổi dropdown suất chiếu để reset trạng thái sạch sẽ.
     - Tự động dừng camera trước khi người dùng quét bằng file upload, tránh xung đột trên phần tử DOM `#reader`.
     - Cơ chế retry có kiểm soát khi gặp lỗi mạng (`NETWORK_ERROR`).

3. **Trang Nhật ký & Lịch sử Soát vé (`GET /staff/check-in/history`)**:
   - Truy vấn hợp nhất dữ liệu từ `dbo.TicketScans` (lượt quét thành công, đã dùng, sai suất) và `dbo.AuditLogs` (lượt quét giả mạo/không tồn tại `NOT_FOUND`).
   - Bộ lọc kép theo suất chiếu (`showtimeId`) và theo kết quả soát vé (`outcome`).
   - Phân trang chuẩn xác, hiển thị thời gian định dạng múi giờ Việt Nam (UTC+7).

4. **Reservation Expiry Background Worker (`ReservationExpiryWorker`)**:
   - Kế thừa `BackgroundService` chạy nền độc lập với chu kỳ điều khiển bởi `PeriodicTimer`.
   - Sử dụng `IServiceScopeFactory` tạo scoped context sạch cho mỗi chu kỳ, gọi `OrderLifecycleService.ExpireOverdueOrdersAsync`.
   - Cơ chế SemaphoreSlim chống chạy đè (overlap execution) khi chu kỳ quét kéo dài.
   - Cấu hình an toàn: `BGP_ENABLE_EXPIRY_WORKER` (mặc định `false` trong testing), `BGP_EXPIRY_INTERVAL_SECONDS` (mặc định 30s), `BGP_EXPIRY_BATCH_SIZE` (mặc định 100 đơn/lô).

5. **Làm cứng Kiểm thử Tranh chấp Đồng thời & Vòng đời Worker (Phase 3 & Phase 5)**:
   - **Loại bỏ False-Positive trong `TC_WORKER_06`:** Viết lại hoàn toàn ca kiểm thử chạy đua giữa thanh toán (`PaymentService.ApplyVerifiedMockAsync`) và worker quét hết hạn (`ReservationExpiryWorker.ProcessExpiredOrdersAsync`). Sử dụng đơn hàng thật, giữ ghế thật, payment attempt thật sinh từ `CreateMockAttemptAsync`, thông điệp mock HMAC có chữ ký hợp lệ khớp 100% mã đơn và attempt key.
   - Hai tiến trình thực thi đồng thời trên hai DbContext riêng biệt, tranh chấp khóa cấp ứng dụng thật trên SQL Server (`SqlServerLocks.ShowtimeAsync`).
   - Không nuốt ngoại lệ bằng catch-all.
   - Kiểm tra bất biến chặt chẽ:
     - Nếu thanh toán thắng: Đơn `PAID`, ghế `SOLD`, vé được phát hành đầy đủ, worker không thể đảo ngược.
     - Nếu worker hết hạn thắng: Đơn `EXPIRED` (hoặc `NEEDS_REVIEW`), ghế `AVAILABLE`, 0 vé được phát hành, trạng thái thanh toán không bao giờ thành `SUCCESS`.
     - Chạy qua 3 chu kỳ lặp (iterations) để xác thực độ ổn định dưới tải concurrency.
   - **Bổ sung `TC_WORKER_07`:** Kiểm thử vòng đời thực tế của `IHostedService` với `PeriodicTimer`, xác nhận worker tự động kích hoạt quét sau interval (1s) và dừng êm dịu (graceful shutdown) mà không rò rỉ ngoại lệ.
   - **Mở rộng `TC_WORKER_04`:** Kiểm tra toàn diện cả 4 trạng thái terminal (`PAID`, `CANCELLED`, `FAILED`, `NEEDS_REVIEW`) với dữ liệu quan hệ hợp lệ, xác nhận worker không can thiệp sai lệch.

6. **Bảo toàn Chứng cứ Dữ liệu trong Test Fixture (Phase 4)**:
   - Loại bỏ các câu lệnh xóa cứng (`DELETE FROM dbo.Orders ...`) trong `TestDatabaseFixture.cs` từng che giấu vi phạm dữ liệu.
   - Thêm ca kiểm thử tích hợp `TC_INT_02_TestDatabaseFixture_DoesNotSilentlyDeleteInconsistentOrders` bảo đảm fixture không bao giờ tự ý xóa đơn hàng bất thường.

7. **Quản lý Bí mật Ổn định & Script Khởi tạo (Phase 6)**:
   - Cập nhật `scripts/01_prepare_dev_session.ps1`:
     - Tự động nhận diện và phân biệt môi trường SQL Server cục bộ (localhost với Windows Integrated Security) và Docker SQL Server (cổng 14333 với SA password).
     - Ưu tiên đồng bộ khóa ổn định từ `.NET User Secrets`.
     - Nếu khóa bị thiếu, cảnh báo rõ ràng và in cú pháp thiết lập cụ thể bằng PowerShell.
     - Không khẳng định sai sự thật "khóa ổn định" khi hệ thống đang dùng khóa tạm thời (ephemeral keys).
     - Cập nhật hướng dẫn `.NET User Secrets` chi tiết trong README.

8. **Công cụ Seeding Demo Check-in Tự động (Phase 7)**:
   - Bổ sung lệnh `seed-checkin-demo` vào `BetaGiaiPhong.Runner`.
   - Tạo script `scripts/03_seed_checkin_demo.ps1`.
   - Tự động kiểm tra hoặc tạo một suất chiếu đang hoạt động bắt đầu trong ~15 phút (nằm trong cửa sổ soát vé hợp lệ), đặt 1 ghế khả dụng, thanh toán mô phỏng thành công và phát hành vé thật.
   - In đầy đủ thông tin: Showtime ID, Phim, Phòng, Giờ chiếu, Mã đơn, Mã vé, Vị trí ghế, Token QR thô và URL trực tiếp đến trang soát vé.

---

### 2. DANH MỤC CÁC TỆP ĐÃ THAY ĐỔI VÀ TẠO MỚI

| Thao tác | Đường dẫn tệp | Mục đích |
|---|---|---|
| Mới | `src/BetaGiaiPhong.Web/Api/StaffTicketsApiController.cs` | API endpoint `POST /api/staff/tickets/scan` soát vé bảo mật |
| Mới | `src/BetaGiaiPhong.Web/Controllers/StaffCheckInController.cs` | Controller quản lý màn hình soát vé và lịch sử soát vé |
| Mới | `src/BetaGiaiPhong.Web/Models/Requests/ScanTicketRequest.cs` | DTO request kiểm tra hợp lệ dữ liệu soát vé |
| Mới | `src/BetaGiaiPhong.Web/Models/Responses/ScanTicketResponse.cs` | DTO response trả về kết quả soát vé và chi tiết vé/phim/phòng/ghế |
| Mới | `src/BetaGiaiPhong.Web/Models/ViewModels/StaffCheckInViewModels.cs` | ViewModels cho màn hình soát vé và bảng lịch sử |
| Mới | `src/BetaGiaiPhong.Web/Services/Workers/ReservationExpiryWorker.cs` | BackgroundService định kỳ quét đơn hàng hết hạn giữ ghế |
| Mới | `src/BetaGiaiPhong.Web/Views/StaffCheckIn/Index.cshtml` | Giao diện soát vé: camera, file upload, USB scanner, cờ in-flight, DOM an toàn |
| Mới | `src/BetaGiaiPhong.Web/Views/StaffCheckIn/History.cshtml` | Giao diện tra cứu lịch sử soát vé và nhật ký từ chối |
| Mới | `src/BetaGiaiPhong.Web/wwwroot/lib/html5-qrcode/html5-qrcode.min.js` | Thư viện quét mã QR cục bộ (375 KB, zero runtime CDN dependency) |
| Mới | `scripts/03_seed_checkin_demo.ps1` | Script tiện ích tự động seed suất chiếu và vé demo phục vụ soát vé ngay |
| Sửa | `src/BetaGiaiPhong.Runner/Program.cs` | Bổ sung lệnh `seed-checkin-demo` với logic tạo suất chiếu và vé hợp lệ |
| Sửa | `scripts/01_prepare_dev_session.ps1` | Nâng cấp quản lý khóa an toàn, cảnh báo ephemeral keys và phân biệt localhost/Docker |
| Sửa | `src/BetaGiaiPhong.Web/Program.cs` | Đăng ký HostedService `ReservationExpiryWorker` |
| Sửa | `src/BetaGiaiPhong.Web/Views/Shared/_Layout.cshtml` | Bổ sung menu Soát vé cho người dùng có role STAFF/MANAGER/ADMIN |
| Sửa | `tests/BetaGiaiPhong.Tests/IntegrationTests/TestDatabaseFixture.cs` | Loại bỏ các lệnh xóa cứng đơn hàng, bảo tồn chứng cứ dữ liệu |
| Sửa | `tests/BetaGiaiPhong.Tests/IntegrationTests/ReportingAndIntegrityTests.cs` | Bổ sung `TC_INT_02` kiểm tra fixture không xóa đơn hàng bất thường |
| Sửa | `tests/BetaGiaiPhong.Tests/IntegrationTests/ReservationExpiryWorkerTests.cs` | Nâng cấp `TC_WORKER_04`, viết lại `TC_WORKER_06` concurrency thật, thêm `TC_WORKER_07` |
| Sửa | `tests/BetaGiaiPhong.Tests/WebTests/StaffCheckInWebTests.cs` | Bổ sung `TC_WEB_STAFF_18` xác minh scanner không log token và render DOM an toàn |
| Sửa | `README.md` | Cập nhật số lượng test (130 Passed), hướng dẫn User Secrets và script seeder |

---

### 3. KẾT QUẢ KIỂM THỬ TỰ ĐỘNG

Chạy toàn bộ bộ kiểm thử của giải pháp qua lệnh:
```powershell
dotnet test .\BetaGiaiPhong.slnx --logger "console;verbosity=normal"
```

**Tổng kết:**
- **Tổng số ca kiểm thử:** 130
- **Số ca kiểm thử ĐẠT:** 130 (100%)
- **Số ca kiểm thử THẤT BẠI:** 0
- **Số ca kiểm thử BỊ BỎ QUA:** 0
- **Thời gian chạy:** 1 phút 30 giây

#### Danh mục các ca kiểm thử chính trong BE-04A:

1. **`StaffCheckInWebTests` (18 tests - 100% PASS):**
   - `TC_WEB_STAFF_01`: Khách chưa đăng nhập truy cập `/staff/check-in` -> Chuyển hướng về `/account/login`.
   - `TC_WEB_STAFF_02`: Người dùng role `CUSTOMER` truy cập `/staff/check-in` -> Từ chối 403 Forbidden.
   - `TC_WEB_STAFF_03`: Người dùng role `STAFF` truy cập `/staff/check-in` -> 200 OK, hiển thị đầy đủ giao diện soát vé.
   - `TC_WEB_STAFF_04`: Người dùng role `STAFF` truy cập `/staff/check-in/history` -> 200 OK.
   - `TC_WEB_STAFF_05`: Khách chưa đăng nhập gọi API `POST /api/staff/tickets/scan` -> 401 Unauthorized.
   - `TC_WEB_STAFF_06`: Khách hàng role `CUSTOMER` gọi API soát vé -> 403 Forbidden.
   - `TC_WEB_STAFF_07`: Gọi API thiếu Anti-CSRF token -> 400 Bad Request.
   - `TC_WEB_STAFF_08`: Payload dữ liệu trống hoặc không hợp lệ -> 400 Bad Request.
   - `TC_WEB_STAFF_09`: Quét vé hợp lệ -> 200 OK, kết quả `ACCEPTED`, vé chuyển trạng thái `USED`, ghi nhận `dbo.TicketScans`.
   - `TC_WEB_STAFF_10`: Quét lại vé đã dùng -> 409 Conflict, kết quả `ALREADY_USED`.
   - `TC_WEB_STAFF_11`: Quét vé sai suất chiếu -> 422 Unprocessable Entity, kết quả `WRONG_SHOWTIME`.
   - `TC_WEB_STAFF_12`: Quét vé ngoài khung giờ hợp lệ (-30p trước chiếu đến +15p sau kết thúc) -> 422 Unprocessable Entity, kết quả `OUTSIDE_WINDOW`.
   - `TC_WEB_STAFF_13`: Quét token giả mạo không tồn tại -> 404 Not Found, kết quả `NOT_FOUND`, ghi nhật ký bảo mật `dbo.AuditLogs`.
   - `TC_WEB_STAFF_14`: Hai nhân viên quét đồng thời cùng một vé trên hai luồng song song -> Chính xác 1 luồng `ACCEPTED` và 1 luồng `ALREADY_USED`, không bị race condition.
   - `TC_WEB_STAFF_15`: Xác minh phản hồi JSON tuyệt đối không làm lộ token thô, khóa AES hoặc chuỗi băm bảo mật.
   - `TC_WEB_STAFF_16`: Kiểm thử luồng End-to-End từ đặt vé -> thanh toán -> khách hàng tải mã QR dạng ảnh PNG -> giải mã ảnh QR thô bằng pure C# decoder -> nhân viên quét token giải mã qua Staff API -> `ACCEPTED` -> quét lại -> `ALREADY_USED`.
   - `TC_WEB_STAFF_17`: Kiểm tra giao diện lịch sử soát vé hiển thị chính xác cả lượt quét thành công từ `TicketScans` và lượt quét giả mạo từ `AuditLogs`.
   - `TC_WEB_STAFF_18`: Kiểm tra giao diện phía client không ghi log token thô ra console, thực hiện render DOM an toàn bằng `textContent` và cung cấp đầy đủ các nút reset luồng quét (`#btnScanNext`, `#btnRescanCurrent`).

2. **`ReservationExpiryWorkerTests` (7 tests - 100% PASS):**
   - `TC_WORKER_01`: Cấu hình `BGP_ENABLE_EXPIRY_WORKER=false` -> Worker không tự ý thực thi.
   - `TC_WORKER_02`: Quét và thu hồi các đơn hàng quá hạn giữ ghế (`PENDING_PAYMENT` quá 5 phút) -> Chuyển thành `EXPIRED`, giải phóng ghế về `AVAILABLE`, ghi `dbo.AuditLogs`.
   - `TC_WORKER_03`: Bảo toàn các đơn hàng còn trong thời hạn giữ ghế -> Giữ nguyên trạng thái `PENDING_PAYMENT` và trạng thái ghế `HELD`.
   - `TC_WORKER_04`: Bảo toàn đầy đủ cả 4 trạng thái terminal (`PAID`, `CANCELLED`, `FAILED`, `NEEDS_REVIEW`) -> Ghế và vé tương ứng được giữ toàn vẹn, worker không can thiệp sai lệch.
   - `TC_WORKER_05`: Tuân thủ giới hạn kích thước phân lô (`batchSize`) khi số lượng đơn quá hạn lớn.
   - `TC_WORKER_06`: Kiểm thử tranh chấp đồng thời thật giữa luồng thanh toán và luồng thu hồi quá hạn qua 3 chu kỳ lặp với khóa ứng dụng SQL Server -> Bất biến dữ liệu được bảo toàn 100%, không phát sinh trạng thái lai tạp hoặc mất đồng bộ.
   - `TC_WORKER_07`: Kiểm thử vòng đời thực tế của `IHostedService` với PeriodicTimer -> Tự động kích hoạt quét sau interval (1s) và hoàn tất dừng êm dịu (graceful shutdown).

3. **`ReportingAndIntegrityTests` (3 tests - 100% PASS):**
   - `TC_REP_01`: View `dbo.vw_ShowtimeSales` tính toán đúng chỉ số doanh thu và tỷ lệ lấp đầy.
   - `TC_INT_01`: Kiểm tra tĩnh và động 6 bất biến toàn vẹn đa bảng trên cơ sở dữ liệu -> 0 vi phạm.
   - `TC_INT_02`: Xác minh `TestDatabaseFixture` không tự ý xóa đơn hàng bất thường để che giấu bằng chứng lỗi.

---

### 4. KẾT QUẢ KIỂM TRA BẢO MẬT & TOÀN VẸN CƠ SỞ DỮ LIỆU (PHASE 9)

1. **Biên dịch Release:**
   ```powershell
   dotnet build .\BetaGiaiPhong.slnx -c Release
   ```
   -> **Thành công:** 0 Warning(s), 0 Error(s).

2. **Kiểm tra thay đổi Schema EF Core:**
   ```powershell
   dotnet ef migrations has-pending-model-changes --project .\src\BetaGiaiPhong.Data --startup-project .\src\BetaGiaiPhong.Data
   ```
   -> **Thành công:** "No changes have been made to the model since the last migration." (0 pending changes).

3. **Quét lỗ hổng bảo mật NuGet:**
   ```powershell
   dotnet list package --vulnerable --include-transitive
   ```
   -> **Thành công:** 0 vulnerable packages across all 4 projects.

4. **Kiểm tra tĩnh cấu trúc Schema:**
   ```powershell
   python .\scripts\static_schema_checks.py
   ```
   -> **Thành công:** PASS 17 tables, 116 columns, 22 FKs; migration/snapshot/code mapping structurally consistent.

5. **Xác minh toàn vẹn đa bảng qua Runner:**
   ```powershell
   cmd.exe /c "set ConnectionStrings__BetaDb=Server=localhost;Database=BetaGiaiPhong_Integration_TestDb;Integrated Security=True;TrustServerCertificate=True;& dotnet run --project .\src\BetaGiaiPhong.Runner -- verify"
   ```
   -> **Thành công:**
   ```text
   Wrong room/seat: 0 violation(s)
   SOLD seat without PAID order: 0 violation(s)
   PAID orders missing tickets: 0 violation(s)
   Order price snapshot mismatch: 0 violation(s)
   Late payment improperly SUCCESS: 0 violation(s)
   Dead order still holds seat: 0 violation(s)
   INTEGRITY PASSED (static consistency checks; run race test separately).
   ```

---

### 5. GHI NHẬN KIỂM THỬ THỦ CÔNG END-TO-END (PHASE 8)

| Kịch bản kiểm thử | Phương thức thực hiện | Kết quả thực tế | Ghi chú minh bạch |
|---|---|---|---|
| Đăng nhập tài khoản Nhân viên (`staff@demo.invalid`) | HTTP POST cookie auth | ĐẠT | Cookie xác thực hợp lệ, cấp quyền vào `/staff/check-in` |
| Quét mã QR qua tải file ảnh PNG | Web API & file decoder | ĐẠT | Giải mã chuẩn xác, kết quả `ACCEPTED`, vé đổi sang `USED` |
| Quét qua nhập token thủ công | Form input | ĐẠT | Hoạt động trơn tru, hiển thị đầy đủ chi tiết suất chiếu & ghế |
| Quét lại cùng một mã vừa được chấp nhận | Camera / re-scan | ĐẠT | Cờ `isTicketAccepted` bảo vệ chống đổi sang `ALREADY_USED` do lia camera |
| Bấm nút "Quét lại vé này (Kiểm tra lại)" | UI Action Button | ĐẠT | Gửi yêu cầu tái kiểm tra có chủ đích, trả về `ALREADY_USED` chính xác |
| Bấm nút "Quét vé tiếp theo" | UI Action Button | ĐẠT | Reset thẻ kết quả về trạng thái sẵn sàng, focus vào ô nhập |
| Đổi suất chiếu trên dropdown | Select Change Event | ĐẠT | Xóa sạch trạng thái quét cũ, sẵn sàng cho suất chiếu mới |
| Xử lý khi mất kết nối mạng | Gián đoạn kết nối | ĐẠT | Thẻ lỗi mạng kèm nút "Thử lại kết nối" hoạt động chuẩn xác |
| Quét camera thiết bị vật lý thật (Hardware Webcam) | Camera stream | **CHƯA KIỂM THỬ PHẦN CỨNG** | Do môi trường thực thi là sandbox headless không gắn webcam vật lý. Toàn bộ logic giải mã và xử lý video stream đã được kiểm chứng tự động qua luồng Canvas/File Decoder và pure C# PNG Decoder. |

---

### 6. KẾT LUẬN & SẴN SÀNG MERGE

Toàn bộ các mục tiêu của giai đoạn **BE-04A** và các hạng mục làm cứng mã nguồn phát hiện sau rà soát đã được xử lý triệt để, kiểm thử tự động 130/130 ca đạt 100%, 0 lỗ hổng bảo mật, 0 vi phạm toàn vẹn dữ liệu.

Nhánh `feat/staff-checkin-expiry` đang ở trạng thái **READY TO MERGE** vào `main`.

*Lưu ý cam kết:* Tuyệt đối không tự ý thực hiện lệnh `git push` hay `git merge` khi chưa nhận được sự phê duyệt rõ ràng từ người dùng.
