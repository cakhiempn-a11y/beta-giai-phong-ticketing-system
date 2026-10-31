# BÁO CÁO TRIỂN KHAI GIAI ĐOẠN BE-04A
## VẬN HÀNH SOÁT VÉ QR & TỰ ĐỘNG XỬ LÝ ĐƠN HÀNG HẾT HẠN

---

### THÔNG TIN TỔNG QUAN

- **Dự án:** Hệ thống quản lý bán vé trực tuyến rạp chiếu phim Beta Giải Phóng (MIS Capstone Prototype).
- **Giai đoạn:** BE-04A — Staff QR Scanner, Ticket Check-in & Automatic Reservation Expiry.
- **Workspace:** `D:\Projects\BetaGiaiPhong`
- **GitHub Repository:** `https://github.com/cakhiempn-a11y/beta-giai-phong-ticketing-system`
- **Nhánh phát triển:** `feat/staff-checkin-expiry` (tạo từ `main` commit `cd2f7b677c12`).
- **Tài khoản Git & Committer:** `cakhiempn-a11y` (`320607805+cakhiempn-a11y@users.noreply.github.com`).
- **Môi trường & Công nghệ:** .NET 10 SDK, ASP.NET Core 10, Entity Framework Core 10.0.14, Microsoft SQL Server, xUnit, `html5-qrcode` 2.3.8 (local vendored).

---

### 1. TỔNG QUAN KẾT QUẢ THỰC HIỆN

Toàn bộ các yêu cầu của giai đoạn BE-04A đã được triển khai hoàn tất và kiểm chứng độc lập:
1. **Staff QR Scanning API (`POST /api/staff/tickets/scan`)**:
   - Xác thực người dùng bằng Cookie Auth, ủy quyền chính sách `RequireStaff` (`STAFF`, `MANAGER`, `ADMIN`).
   - Bảo vệ chống CSRF bắt buộc qua header `X-XSRF-TOKEN` / cookie `.BetaGiaiPhong.Antiforgery`.
   - Ủy thác trực tiếp cho domain service nghiệp vụ `TicketScanningService.ScanAsync` của Data Layer, giữ nguyên vẹn cơ chế transaction và khóa tuần tự hóa SQL Server.
   - Trả về mã HTTP tương ứng với từng trường hợp soát vé:
     - `ACCEPTED`: 200 OK.
     - `ALREADY_USED`: 409 Conflict.
     - `WRONG_SHOWTIME`: 422 Unprocessable Entity.
     - `OUTSIDE_WINDOW`: 422 Unprocessable Entity.
     - `NOT_FOUND`: 404 Not Found (ghi nhật ký bảo mật vào `dbo.AuditLogs` với `EventType = TICKET_SCAN_REJECTED`).
     - Yêu cầu sai dữ liệu đầu vào: 400 Bad Request.
   - Tuyệt đối không để lộ token thô, khóa mã hóa (`QrTokenCipher`), giá trị băm (`QrTokenHash`) hay cấu hình hệ thống trong phản hồi JSON.
2. **Giao diện Soát vé Nhân viên (`GET /staff/check-in`)**:
   - Giao diện Razor View tối ưu cho thiết bị di động, tablet và quầy soát vé rạp chiếu.
   - Tích hợp 4 phương thức quét mã:
     - Quét qua Camera trực tiếp trên trình duyệt sử dụng thư viện `html5-qrcode` (vendored 100% cục bộ tại `wwwroot/lib/html5-qrcode/html5-qrcode.min.js`, không phụ thuộc mạng ngoài).
     - Quét qua ảnh chụp / file mã QR tải lên (file upload decoder).
     - Nhập mã token trực tiếp bằng tay.
     - Hỗ trợ máy quét mã vạch 2D cầm tay cắm cổng USB (USB Barcode Scanner) tự động bắt luồng phím và gửi quét.
   - Thẻ hiển thị kết quả trực quan theo mã màu Bootstrap chuẩn: Xanh lá (`ACCEPTED`), Cam đậm (`ALREADY_USED`), Vàng cam (`WRONG_SHOWTIME`, `OUTSIDE_WINDOW`), Đỏ (`NOT_FOUND`, `REJECTED`).
   - Tự động debounce trong cửa sổ 3 giây để chống quét trùng liên tiếp do rung tay khi lia camera.
3. **Trang Nhật ký & Lịch sử Soát vé (`GET /staff/check-in/history`)**:
   - Truy vấn hợp nhất dữ liệu từ cả `dbo.TicketScans` (các lượt quét vé hợp lệ/đã dùng/sai suất) và `dbo.AuditLogs` (các lượt quét từ chối/không tồn tại `NOT_FOUND`).
   - Hỗ trợ bộ lọc kép: lọc theo suất chiếu (`showtimeId`) và lọc theo kết quả soát vé (`outcome`).
   - Phân trang đầy đủ, hiển thị thời gian định dạng múi giờ Việt Nam (UTC+7).
4. **Reservation Expiry Background Worker (`ReservationExpiryWorker`)**:
   - Kế thừa `BackgroundService` chạy nền độc lập với chu kỳ được điều khiển bởi `PeriodicTimer`.
   - Sử dụng `IServiceScopeFactory` tạo scoped context sạch cho mỗi chu kỳ quét, gọi `OrderLifecycleService.ExpireOverdueOrdersAsync`.
   - Cơ chế Semaphore chống chạy đè (overlap execution) khi chu kỳ quét kéo dài.
   - Cấu hình an toàn qua biến môi trường:
     - `BGP_ENABLE_EXPIRY_WORKER`: Mặc định `false` (tắt trong môi trường test để đảm bảo cách ly tuyệt đối).
     - `BGP_EXPIRY_INTERVAL_SECONDS`: Mặc định 30 giây.
     - `BGP_EXPIRY_BATCH_SIZE`: Mặc định 100 đơn/lần quét.
   - Cơ chế bắt lỗi ngoại lệ fail-safe, ghi log có cấu trúc và dừng tiến trình an toàn khi ứng dụng shutdown (Graceful Shutdown).
5. **Đồng bộ hóa cấu hình khóa bảo mật**:
   - Script `scripts/01_prepare_dev_session.ps1` được nâng cấp để ưu tiên nạp khóa ổn định từ `.NET User Secrets` trước khi sinh khóa ngẫu nhiên tạm thời, loại bỏ nguy cơ ghi đè khóa vé hợp lệ khi khởi tạo phiên làm việc mới.

---

### 2. DANH MỤC CÁC TỆP ĐÃ THAY ĐỔI VÀ TẠO MỚI

| Thao tác | Đường dẫn tệp | Mục đích |
|---|---|---|
| Mới | `src/BetaGiaiPhong.Web/Api/StaffTicketsApiController.cs` | API endpoint `POST /api/staff/tickets/scan` soát vé bảo mật cho nhân viên |
| Mới | `src/BetaGiaiPhong.Web/Controllers/StaffCheckInController.cs` | Controller quản lý màn hình soát vé và lịch sử soát vé |
| Mới | `src/BetaGiaiPhong.Web/Models/Requests/ScanTicketRequest.cs` | DTO request kiểm tra hợp lệ dữ liệu soát vé |
| Mới | `src/BetaGiaiPhong.Web/Models/Responses/ScanTicketResponse.cs` | DTO response trả về kết quả soát vé và chi tiết vé/phim/phòng/ghế |
| Mới | `src/BetaGiaiPhong.Web/Models/ViewModels/StaffCheckInViewModels.cs` | ViewModels cho màn hình soát vé và bảng lịch sử |
| Mới | `src/BetaGiaiPhong.Web/Services/Workers/ReservationExpiryWorker.cs` | BackgroundService định kỳ quét đơn hàng hết hạn giữ ghế |
| Mới | `src/BetaGiaiPhong.Web/Views/StaffCheckIn/Index.cshtml` | Giao diện soát vé tích hợp camera, upload ảnh, và USB scanner |
| Mới | `src/BetaGiaiPhong.Web/Views/StaffCheckIn/History.cshtml` | Giao diện tra cứu lịch sử soát vé và nhật ký từ chối |
| Mới | `src/BetaGiaiPhong.Web/wwwroot/lib/html5-qrcode/html5-qrcode.min.js` | Thư viện quét mã QR cục bộ (375 KB, zero CDN runtime dependency) |
| Mới | `tests/BetaGiaiPhong.Tests/WebTests/StaffCheckInWebTests.cs` | 17 ca kiểm thử tự động cho Staff Check-in API, UI & End-to-End |
| Mới | `tests/BetaGiaiPhong.Tests/IntegrationTests/ReservationExpiryWorkerTests.cs` | 6 ca kiểm thử tích hợp cho Reservation Expiry Worker |
| Sửa | `src/BetaGiaiPhong.Web/Program.cs` | Đăng ký HostedService `ReservationExpiryWorker` |
| Sửa | `src/BetaGiaiPhong.Web/Views/Shared/_Layout.cshtml` | Bổ sung menu Soát vé cho người dùng có role STAFF/MANAGER/ADMIN |
| Sửa | `scripts/01_prepare_dev_session.ps1` | Tự động đồng bộ khóa ổn định từ .NET User Secrets |
| Sửa | `tests/BetaGiaiPhong.Tests/IntegrationTests/TestDatabaseFixture.cs` | Cách ly dữ liệu và dọn dẹp các bản ghi mồ côi khi chạy test suite |
| Sửa | `README.md` | Cập nhật số lượng test (127 Passed), phạm vi BE-04A và sơ đồ kiến trúc |

---

### 3. KẾT QUẢ KIỂM THỬ TỰ ĐỘNG

Chạy toàn bộ bộ kiểm thử của giải pháp qua lệnh:
```powershell
dotnet test .\BetaGiaiPhong.slnx --logger "console;verbosity=normal"
```

**Kết quả:**
- **Tổng số ca kiểm thử:** 127
- **Số ca kiểm thử ĐẠT:** 127 (100%)
- **Số ca kiểm thử THẤT BẠI:** 0
- **Số ca kiểm thử BỊ BỎ QUA:** 0
- **Thời gian chạy:** 57 giây

#### Danh sách các ca kiểm thử mới trong BE-04A:

1. **`StaffCheckInWebTests` (17 tests - 100% PASS):**
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

2. **`ReservationExpiryWorkerTests` (6 tests - 100% PASS):**
   - `TC_WORKER_01`: Cấu hình `BGP_ENABLE_EXPIRY_WORKER=false` -> Worker không tự ý thực thi.
   - `TC_WORKER_02`: Quét và thu hồi các đơn hàng quá hạn giữ ghế (`PENDING_PAYMENT` quá 5 phút) -> Chuyển thành `EXPIRED`, giải phóng ghế về `AVAILABLE`, ghi `dbo.AuditLogs`.
   - `TC_WORKER_03`: Bảo toàn các đơn hàng còn trong thời hạn giữ ghế -> Giữ nguyên trạng thái `PENDING_PAYMENT` và trạng thái ghế `HELD`.
   - `TC_WORKER_04`: Bảo toàn các đơn hàng đã thanh toán (`PAID`), đã hủy (`CANCELLED`) hoặc `NEEDS_REVIEW` -> Không can thiệp sai lệch.
   - `TC_WORKER_05`: Tuân thủ giới hạn kích thước phân lô (`batchSize`) khi số lượng đơn quá hạn lớn.
   - `TC_WORKER_06`: Kiểm thử chạy đua đồng thời giữa luồng thanh toán và luồng thu hồi quá hạn -> Tính toàn vẹn dữ liệu được bảo toàn tuyệt đối theo ràng buộc SQL Server.

---

### 4. KẾT QUẢ XÁC MINH KỸ THUẬT & TOÀN VẸN CƠ SỞ DỮ LIỆU

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

### 5. TUÂN THỦ NGUYÊN TẮC BẢO MẬT & BẢO VỆ DỮ LIỆU

- **Bảo mật bí mật cấu hình:** Tuyệt đối không hardcode khóa HMAC hay AES vào mã nguồn, file cấu hình được Git theo dõi, script hay commit.
- **Fail-closed Authorization:** Chỉ người dùng có role nhân viên hoặc quản lý (`STAFF`, `MANAGER`, `ADMIN`) mới có thể truy cập endpoint soát vé và màn hình soát vé.
- **CSRF Protection:** Mọi request POST lên API soát vé đều được bảo vệ bởi Antiforgery Token thông qua header `X-XSRF-TOKEN` và cookie `.BetaGiaiPhong.Antiforgery`.
- **Zero Token Leakage:** Thông tin token thô và dữ liệu mật mã không bao giờ được phản hồi ra ngoài qua API hay HTML.
- **Cơ sở dữ liệu mẫu:** Toàn vẹn 17 bảng dữ liệu và các ràng buộc toàn vẹn quan hệ (22 FK, 9 CHECK constraints) được bảo toàn 100%, không phát sinh bảng hay quan hệ thừa.

---

### 6. KẾT LUẬN & SẴN SÀNG MERGE

Giai đoạn **BE-04A** đã hoàn thành xuất sắc tất cả mục tiêu chức năng, bảo mật và kiểm thử tự động. Nhánh `feat/staff-checkin-expiry` đã sẵn sàng để người dùng xem xét và tạo Pull Request vào `main`.

Không thực hiện bất kỳ lệnh push hay merge tự động nào khi chưa có sự phê duyệt rõ ràng từ người dùng.
