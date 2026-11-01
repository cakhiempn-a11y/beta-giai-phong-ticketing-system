# BÁO CÁO TRIỂN KHAI & CHẤT LƯỢNG GIAI ĐOẠN BE-05A
## PHÂN LẬP CƠ SỞ DỮ LIỆU KIỂM THỬ, TÍCH HỢP LIÊN TỤC GITHUB ACTIONS CI & BẢO ĐẢM HỒI QUY

---

### THÔNG TIN TỔNG QUAN

- **Dự án:** Hệ thống quản lý bán vé trực tuyến rạp chiếu phim Beta Giải Phóng (MIS Capstone Prototype).
- **Giai đoạn:** BE-05A — SQL Test Isolation, GitHub Actions CI, Security & Regression Assurance.
- **Workspace:** `D:\Projects\BetaGiaiPhong`
- **GitHub Repository:** `https://github.com/cakhiempn-a11y/beta-giai-phong-ticketing-system`
- **Nhánh phát triển:** `feat/be05a-ci-test-isolation` (tạo từ `main` commit `be7d465`).
- **Tài khoản Git & Committer:** `cakhiempn-a11y` (`320607805+cakhiempn-a11y@users.noreply.github.com`).
- **Nền tảng công nghệ:** .NET 10 SDK, ASP.NET Core 10, Entity Framework Core 10.0.14, Microsoft SQL Server 2022, GitHub Actions, xUnit, Coverlet.

---

### 1. TỔNG QUAN CÁC HẠNG MỤC HOÀN THÀNH

Giai đoạn BE-05A thiết lập một quy trình kiểm thử độc lập, lặp lại được, chạy ổn định trên cả môi trường máy phát triển Windows và môi trường Linux GitHub Actions runner:

1. **Phase 1 — Tái cấu trúc phân lập cơ sở dữ liệu kiểm thử (SQL Server Test Isolation):**
   - Loại bỏ hardcode connection string cố định `BetaGiaiPhong_Integration_TestDb` và Windows Integrated Authentication.
   - Cơ chế sinh định danh động duy nhất theo lượt chạy: `DatabaseName = $"{prefix}{runId}"` (ví dụ `BetaGiaiPhong_Test_20261101_040854_ba294182`).
   - Hỗ trợ đa nền tảng linh hoạt qua các biến môi trường:
     - `BGP_TEST_SQL_MASTER_CONNECTION`: Chuỗi kết nối đến `master` (hỗ trợ Windows LocalDB/SQL Server, Docker container, Linux runner).
     - `BGP_TEST_ALLOW_DB_CREATE`: Cờ cho phép tạo CSDL (mặc định: `true`).
     - `BGP_TEST_DATABASE_PREFIX`: Tiền tố tên CSDL kiểm thử (mặc định: `BetaGiaiPhong_Test_`).
     - `BGP_TEST_AUTO_CLEANUP_DB`: Tự động thu hồi và DROP CSDL sau khi hoàn tất kiểm thử (mặc định: `true`).
   - **Rào chắn an toàn (Safety Gates):**
     - Cấm tuyệt đối kết nối đến database Production (kiểm tra `prod`/`production`).
     - Cấm tuyệt đối chạy test trên database phát triển `BetaGiaiPhong_Dev` / `BetaGiaiPhongDev`.
     - Cấm tự động xóa database cố định cũ `BetaGiaiPhong_Integration_TestDb`.
     - Khử khuẩn tên database với regex `^[a-zA-Z0-9_]{1,64}$`, ngăn chặn triệt để SQL Injection.
     - Kiểm tra quyền sở hữu (ownership verification) trước khi DROP: chỉ cho phép hủy CSDL ephemeral do chính lượt chạy hiện tại tạo ra.

2. **Phase 2 — Cô lập cấu hình, bảo mật Secret & Background Worker:**
   - Bảo đảm các khóa mật mã kiểm thử (`BGP_MOCK_HMAC_KEY_B64`, `BGP_QR_AES_KEY_B64`) được cách ly hoàn toàn, không ghi ra log.
   - Thẩm định `CustomWebApplicationFactory`: Tiêm cấu hình trực tiếp qua in-memory configuration, loại bỏ xung đột môi trường toàn tiến trình.
   - **Cô lập Background Worker:** Trong `CustomWebApplicationFactory`, gỡ bỏ `ReservationExpiryWorker` khỏi danh sách `HostedService` để tránh tình trạng worker chạy ngầm định kỳ quét và làm hết hạn các đơn hàng kiểm thử một cách không kiểm soát; các bài test worker chuyên biệt (`ReservationExpiryWorkerTests`) tự khởi tạo worker độc lập qua DI scope factory.

3. **Phase 3 — Xây dựng GitHub Actions CI Pipeline (`.github/workflows/ci.yml`):**
   - **Trigger:** Kích hoạt tự động trên sự kiện `pull_request` vào `main`, `push` vào `main`, và cho phép chạy thủ công qua `workflow_dispatch`.
   - **Job 1 — Build & Static Validation (`ubuntu-latest`):**
     - Cài đặt .NET 10 SDK theo `global.json`.
     - Phục hồi công cụ .NET cục bộ (`dotnet tool restore` &rarr; `dotnet-ef 10.0.14`).
     - Restore dependencies và Build cấu hình Release (`0 errors, 0 warnings`).
     - Kiểm tra tĩnh lược đồ SQL qua `python scripts/static_schema_checks.py`.
     - Kiểm tra độ trôi mô hình dữ liệu (`dotnet ef migrations has-pending-model-changes`).
     - Quét bảo mật các gói phụ thuộc (`dotnet list package --vulnerable --include-transitive`).
   - **Job 2 — Automated Tests with SQL Server 2022 (`ubuntu-latest`):**
     - Service container `mcr.microsoft.com/mssql/server:2022-latest` có healthcheck rõ ràng.
     - Khởi tạo CSDL kiểm thử ephemeral, chạy toàn bộ bộ kiểm thử tích hợp và Web tests.
     - Thu thập kết quả kiểm thử dạng `test-results.trx` và độ phủ mã nguồn `coverage.cobertura.xml` đính kèm artifact.
   - **Job 3 — Integrity Checks & Runner Validation (`ubuntu-latest`):**
     - Chạy toàn bộ quy trình trên CSDL riêng biệt `BetaGiaiPhong_CI_Integrity`:
       `migrate` &rarr; `seed-demo` &rarr; `smoke` &rarr; `race` &rarr; `verify`.
     - Xác thực 9 ràng buộc bất biến đa bảng đạt chuẩn 0 vi phạm.

4. **Phase 4 — Nâng cao chất lượng kiểm thử & Loại bỏ Race Condition:**
   - **A. Khóa thứ bậc Movie & Showtime:**
     - Bổ sung `SqlServerLocks.MovieAsync` (`SELECT [movie_id] FROM dbo.[Movies] WITH (UPDLOCK, HOLDLOCK)`).
     - Thiết lập thứ tự khóa nghiêm ngặt: `Movie` &rarr; `Room` trong `ShowtimePublishingService.PublishAsync`, và khóa `Movie` trong `MovieManagementService.ArchiveMovieAsync`.
     - Bổ sung ca kiểm thử tương tranh `TC_MGMT_29`: Hai tác vụ đồng thời lưu trữ phim (Archive) và xuất bản suất chiếu (Publish). Kết quả chứng minh bất biến toàn vẹn: Không bao giờ tồn tại phim `ARCHIVED` mà có suất chiếu tương lai `ON_SALE`.
   - **B. Đối chiếu tài chính độc lập cho Bảng điều khiển (Independent Financial Assertions):**
     - Bổ sung ca kiểm thử `TC_MGMT_28`: Tạo tập dữ liệu mô phỏng gồm các đơn `PAID`, `CANCELLED`, `EXPIRED`, `FAILED`, `NEEDS_REVIEW`, webhook thành công lặp lại, hai phòng có sức chứa khác nhau, và các suất chiếu nằm trên ranh giới chuyển ngày giữa múi giờ UTC và giờ Việt Nam (UTC+7).
     - Đối chiếu trực tiếp các chỉ số doanh thu, số vé, tỷ lệ lấp đầy bình quân có trọng số độc lập hoàn toàn với view `dbo.vw_ShowtimeSales`.

---

### 2. KẾT QUẢ KIỂM ĐỊNH CHẤT LƯỢNG TOÀN BỘ HỆ THỐNG

| Tiêu chí kiểm định | Lệnh thực thi | Kết quả thực tế |
|---|---|:---:|
| **Biên dịch Release** | `dotnet build .\BetaGiaiPhong.slnx -c Release` | **PASS (0 Errors, 0 Warnings)** |
| **Độ trôi mô hình EF Core** | `dotnet ef migrations has-pending-model-changes --project .\src\BetaGiaiPhong.Data --startup-project .\src\BetaGiaiPhong.Data` | **PASS (0 pending changes, 0 migrations mới)** |
| **Quét lỗ hổng bảo mật NuGet** | `dotnet list package --vulnerable --include-transitive` | **PASS (0 vulnerable packages)** |
| **Kiểm tra tĩnh cấu trúc CSDL** | `python .\scripts\static_schema_checks.py` | **PASS (17 bảng, 116 cột, 22 FKs)** |
| **Bộ kiểm thử tự động (Lượt 1)** | `dotnet test .\BetaGiaiPhong.slnx` | **PASS: 161 / 161 tests (100%)** |
| **Bộ kiểm thử tự động (Lượt 2)** | `dotnet test .\BetaGiaiPhong.slnx` | **PASS: 161 / 161 tests (100%) — Chứng minh tính lặp lại độc lập** |
| **Toàn vẹn bất biến đa bảng** | `dotnet run --project .\src\BetaGiaiPhong.Runner -- verify` | **PASS: 9/9 checks 0 violations (`INTEGRITY PASSED`)** |

---

### 3. THỐNG KÊ ĐỘ PHỦ MÃ NGUỒN (CODE COVERAGE SUMMARY)

Dữ liệu trích xuất từ `TestResults/coverage.cobertura.xml`:

- **Tổng độ phủ dòng (Overall Line Rate):** **86.78%**
- **Độ phủ phân nhánh (Branch Rate):** **55.49%**
- **Chi tiết theo từng module dự án:**
  - `BetaGiaiPhong.Data`: **97.85%** (Tầng Domain Services, Persistence và Data Rules được bao phủ gần như tuyệt đối).
  - `BetaGiaiPhong.Web`: **74.04%** (Tầng Controllers, ViewModels, Middleware và Authorization Policies).
  - `BetaGiaiPhong.Runner`: **18.55%** (Công cụ CLI phụ trợ độc lập).

---

### 4. PHÂN LOẠI CÁC CA KIỂM THỬ TỰ ĐỘNG (161 TESTS)

Toàn bộ 161 ca kiểm thử được phân bổ chi tiết như sau:

1. **Unit Tests (3 tests):**
   - `MockPaymentSecurityTests`: Xác thực chữ ký HMAC-SHA256, phát hiện giả mạo payload.
   - `NavigationPropertyMappingTests`: Xác thực cấu trúc ánh xạ quan hệ thực thể EF Core.
   - `QrTokenProtectorTests`: Mã hóa và giải mã bảo mật AES-256-GCM cho vé điện tử QR.

2. **Integration Tests (25 tests):**
   - `BookingConcurrencyTests` (4 tests): 20 đến 50 luồng cạnh tranh đặt cùng một ghế tại mức cô lập `Serializable`.
   - `PaymentWorkflowTests` (6 tests): Xử lý webhook thanh toán, idempotency, hết hạn giữ chỗ và late payment.
   - `QrScanningTests` (6 tests): Check-in vé, chống quét lặp, quét đồng thời đa nhân viên.
   - `ReportingAndIntegrityTests` (2 tests): Kiểm tra 9 ràng buộc toàn vẹn cơ sở dữ liệu và xác thực không xóa dữ liệu tùy tiện.
   - `ReservationExpiryWorkerTests` (7 tests): Background worker quét và giải phóng ghế hết hạn định kỳ.

3. **Web Application Tests via WebApplicationFactory (133 tests):**
   - `AuthenticationAndAuthorizationWebTests` (18 tests): Đăng nhập, cookie, phân quyền vai trò (`CUSTOMER`, `STAFF`, `MANAGER`, `ADMIN`).
   - `PublicCatalogWebTests` (16 tests): Duyệt danh mục phim, lịch chiếu, sơ đồ ghế công khai.
   - `BookingAndOrderWebTests` (20 tests): Đặt ghế, countdown timer, hủy đơn, kiểm tra quyền sở hữu IDOR.
   - `MockPaymentAndTicketWebTests` (26 tests): Mô phỏng thanh toán, webhook, render mã QR không lộ bí mật.
   - `StaffCheckInWebTests` (14 tests): Giao diện quét vé nhân viên, kiểm tra chống rò rỉ token phía client.
   - `WebSecurityTests` (10 tests): Anti-CSRF, Rate Limiting, HTTP Security Headers, Content-Type sniffing.
   - `ManagementWebTests` (29 tests): Vòng đời phim, công bố suất chiếu, bảng điều khiển doanh thu, thu hồi quyền động, tương tranh Archive/Publish và đối chiếu số học độc lập.

---

### 5. CÁC RỦI RO ĐÃ ĐƯỢC NHẬN DIỆN VÀ BIỆN PHÁP KIỂM SOÁT

1. **Rủi ro rò rỉ tài nguyên CSDL kiểm thử:**
   - *Kiểm soát:* `TestDatabaseFixture.DisposeAsync` tự động thu hồi kết nối và DROP database ephemeral sau mỗi lần chạy; nếu cleanup gián đoạn, tiền tố `BetaGiaiPhong_Test_` cho phép nhận diện và dọn dẹp an toàn mà không ảnh hưởng CSDL phát triển.
2. **Rủi ro tương tranh giữa kiểm thử và Background Service:**
   - *Kiểm soát:* `CustomWebApplicationFactory` chủ động gỡ bỏ `ReservationExpiryWorker` khỏi web host trong các bài kiểm thử web, ngăn ngừa triệt để hiện tượng đơn hàng bị worker hủy ngoài ý muốn.
3. **Rủi ro môi trường Linux CI thiếu công cụ EF Core CLI:**
   - *Kiểm soát:* Cam kết tệp `dotnet-tools.json` ở thư mục gốc và thực thi `dotnet tool restore` trước các bước kiểm tra model drift.

---

### 6. KẾT LUẬN & ĐÁNH GIÁ SẴN SÀNG (VERDICT)

Nhánh `feat/be05a-ci-test-isolation` đã hoàn thành 100% các mục tiêu kiến trúc và chất lượng đặt ra:
- **Cơ sở dữ liệu kiểm thử:** Độc lập hoàn toàn, hỗ trợ Linux CI runner và Windows dev machine.
- **Tích hợp liên tục:** CI workflow chuẩn 3 jobs (Build, Tests, Integrity).
- **Bộ kiểm thử:** Đạt 161/161 tests PASS với độ bao phủ cao (86.78%).
- **Lược đồ CSDL:** Giữ nguyên vẹn 17 bảng, 22 khóa ngoại.

**TRẠNG THÁI: READY FOR PR**.
