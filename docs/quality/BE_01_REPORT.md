# Báo Cáo Kỹ Thuật Giai Đoạn BE-01
## Nền Tảng ASP.NET Core, Xác Thực, Phân Quyền & Danh Mục Công Khai

**Dự án:** Phân tích, thiết kế và xây dựng hệ thống quản lý bán vé trực tuyến tại rạp chiếu phim Beta Giải Phóng  
**Bối cảnh học thuật:** Đề án tốt nghiệp Hệ thống thông tin quản lý (MIS) — Dữ liệu mô phỏng, không liên kết hệ thống thực tế của Beta Cinemas  
**Phiên bản:** BE-01 Baseline  
**Ngày hoàn thành:** 31/10/2026  
**Trạng thái:** HOÀN THÀNH TOÀN DIỆN (46/46 Tests Passed, 0 Vulnerabilities, 0 Model Drift)

---

## 1. Tóm Tắt Thực Thi (Executive Summary)

Giai đoạn **BE-01** đã thiết lập thành công nền tảng Web Host đầu tiên cho hệ sinh thái Backend của dự án rạp chiếu phim Beta Giải Phóng bằng ASP.NET Core (.NET 10). Dự án tuân thủ nghiêm ngặt mô hình kiến trúc Modular Monolith, tích hợp trực tiếp với Data Layer (`BetaGiaiPhong.Data`) và cơ sở dữ liệu Microsoft SQL Server hiện có mà không làm phát sinh bất kỳ thay đổi schema hay migration mới nào.

### Các kết quả chính đạt được:
1. **Scaffold Web Host:** Khởi tạo project `BetaGiaiPhong.Web` (.NET 10 MVC & RESTful API), tích hợp vào solution `BetaGiaiPhong.slnx`.
2. **Tái sử dụng Data Layer:** Đăng ký toàn bộ DbContext và Domain Services qua `AddBetaCinemaData()` với lifetime Scoped/Singleton chuẩn mực.
3. **Xác thực & Phân quyền không xâm lấn:** Xây dựng hệ thống Cookie Authentication và Role Authorization trực tiếp trên 3 bảng `dbo.Users`, `dbo.Roles`, `dbo.UserRoles`, sử dụng thuật toán PBKDF2 (`PasswordHasher<AppUser>`). Không cài đặt bảng mặc định của ASP.NET Core Identity.
4. **Phân quyền theo Role:** Cấu hình 4 policy `RequireCustomer`, `RequireStaff`, `RequireManager`, `RequireAdmin`.
5. **Danh mục phim & Lịch chiếu công khai:** Cung cấp giao diện Razor Views tiếng Việt và REST API JSON để duyệt phim đã xuất bản (`PUBLISHED`), chi tiết phim và lịch chiếu gom nhóm theo ngày (múi giờ Việt Nam `UTC+7`, định dạng `dd/MM/yyyy HH:mm`).
6. **Sơ đồ ghế chỉ đọc (Read-Only Seat Map):** Hiển thị trực quan mặt bằng ghế phòng chiếu theo hàng A-G, tính toán chính xác trạng thái ghế (`AVAILABLE`, `HELD`, `SOLD`, `BLOCKED`) và nhận diện ghế giữ chỗ hết hạn (expired hold reclamation preview) mà không làm thay đổi dữ liệu database trong luồng GET.
7. **Bảo mật & Middleware phòng thủ:** Tích hợp Correlation ID (`X-Correlation-ID`), Global Exception Handler trả về RFC 7807 `ProblemDetails` cho API, Anti-CSRF (`ValidateAntiForgeryToken`), Rate Limiting (Fixed Window) chống brute force đăng nhập, và các HTTP Security Headers (`nosniff`, `DENY` framing, `strict-origin-when-cross-origin`).
8. **Kiểm thử tự động mở rộng:** Bổ sung 18 automated integration tests trong `BetaGiaiPhong.Tests/WebTests` sử dụng `WebApplicationFactory`, nâng tổng số test của solution lên **46 tests** (toàn bộ 46 tests đều PASS 100%).

---

## 2. Xử Lý Sai Khác Kỹ Thuật Trước Triển Khai (Discrepancy Rectifications)

Trước khi viết code cho BE-01, ba điểm sai khác giữa tài liệu cũ và mã nguồn thực tế đã được rà soát và chuẩn hóa:

### 2.1. Sai khác A — Cơ chế Transaction Locking
- **Báo cáo cũ:** Mô tả `IsolationLevel.ReadCommitted` và row-level lock trên bảng `dbo.ShowtimeSeats`.
- **Thực tế mã nguồn:** `SeatReservationService.cs` sử dụng `IsolationLevel.Serializable`, kết hợp với câu lệnh khóa dòng suất chiếu trong `Shared.cs`:
  ```sql
  SELECT showtime_id, room_id, start_utc, end_utc, status
  FROM dbo.Showtimes WITH (UPDLOCK, HOLDLOCK)
  WHERE showtime_id = @ShowtimeId;
  ```
- **Quyết định kỹ thuật:** Giữ nguyên 100% mã nguồn đang chạy thực tế vì phương án này đã vượt qua kiểm thử stress-concurrency 50 luồng đồng thời (`TC_CON_01c`). Cập nhật lại tài liệu `README.md` và `REFACTORING_REPORT.md` để khớp hoàn toàn với mã nguồn.

### 2.2. Sai khác B — Khung giờ quét mã QR (Scan Window)
- **Báo cáo cũ:** README ghi nhận khung giờ từ -60 phút trước giờ chiếu đến +30 phút sau giờ bắt đầu.
- **Thực tế mã nguồn:** `TicketScanningService.cs` thực thi quy tắc nghiệp vụ:
  ```csharp
  if (now < showtime.StartUtc.AddMinutes(-30) || now > showtime.EndUtc.AddMinutes(15))
  {
      return TicketScanOutcome.OutsideWindow(ticket.TicketId, showtime.ShowtimeId, now, showtime.StartUtc, showtime.EndUtc);
  }
  ```
- **Quyết định kỹ thuật:** Giữ nguyên quy tắc kiểm thử thực tế (từ 30 phút trước giờ bắt đầu đến 15 phút sau giờ kết thúc chiếu) và chỉnh sửa lại tài liệu `README.md` và `REFACTORING_REPORT.md`.

### 2.3. Sai khác C — Tên biến môi trường bí mật (Environment Variables)
- **Báo cáo cũ:** Tài liệu đề cập `BGP_HMAC_SECRET`.
- **Thực tế mã nguồn:** `MockPaymentSecurity.cs` đọc biến `BGP_MOCK_HMAC_KEY_B64`, và `QrTokenProtector.cs` đọc biến `BGP_QR_AES_KEY_B64`.
- **Quyết định kỹ thuật:** Chuẩn hóa toàn bộ tài liệu theo đúng định danh biến môi trường được hỗ trợ trong code: `BGP_MOCK_HMAC_KEY_B64` và `BGP_QR_AES_KEY_B64`.

---

## 3. Kiến Trúc & Cấu Trúc Thành Phần BE-01

```text
src/BetaGiaiPhong.Web/
├── Controllers/
│   ├── HomeController.cs              # Home, Privacy, Error, AccessDenied
│   ├── MoviesController.cs            # Danh sách phim, chi tiết phim & suất chiếu
│   ├── ShowtimesController.cs         # Sơ đồ ghế trực quan (Read-only)
│   └── AccountController.cs           # Login GET/POST, Logout POST, Profile GET
├── Api/
│   ├── MoviesApiController.cs         # GET /api/movies, /api/movies/{id}, /api/movies/{id}/showtimes
│   ├── ShowtimesApiController.cs      # GET /api/showtimes/{id}, /api/showtimes/{id}/seats
│   └── MeApiController.cs             # GET /api/me (Yêu cầu đăng nhập, trả về UserProfileDto)
├── Models/
│   ├── Requests/
│   │   └── LoginRequest.cs            # Request model cho đăng nhập
│   ├── Responses/
│   │   ├── MovieDto.cs                # DTO danh sách phim
│   │   ├── MovieDetailDto.cs          # DTO chi tiết phim
│   │   ├── ShowtimeDto.cs             # DTO suất chiếu gom nhóm theo ngày
│   │   ├── ShowtimeDetailDto.cs       # DTO chi tiết suất chiếu và giá vé theo hạng
│   │   ├── SeatMapDto.cs              # DTO sơ đồ ghế (hàng, cột, trạng thái, giá)
│   │   └── UserProfileDto.cs          # DTO thông tin người dùng và vai trò
│   └── ViewModels/
│       ├── HomeIndexViewModel.cs      # ViewModel trang chủ
│       ├── MovieDetailViewModel.cs    # ViewModel chi tiết phim & nhóm suất chiếu theo ngày
│       ├── SeatMapViewModel.cs        # ViewModel sơ đồ ghế phòng chiếu
│       ├── LoginViewModel.cs          # ViewModel form đăng nhập & thông báo lỗi
│       └── UserProfileViewModel.cs    # ViewModel trang hồ sơ cá nhân
├── Services/
│   ├── IAuthService.cs                # Hợp đồng nghiệp vụ xác thực
│   ├── AuthService.cs                 # Hiện thực xác thực PBKDF2 trên dbo.Users/Roles
│   ├── ICatalogService.cs             # Hợp đồng truy vấn danh mục và sơ đồ ghế
│   └── CatalogService.cs              # Hiện thực truy vấn LINQ, timezone và seat map logic
├── Security/
│   ├── CorrelationIdMiddleware.cs     # Middleware sinh & lan truyền X-Correlation-ID
│   └── GlobalExceptionHandlerMiddleware.cs # Middleware chuẩn hóa lỗi RFC 7807 ProblemDetails
├── Views/
│   ├── Home/
│   │   ├── Index.cshtml               # Hero banner, danh sách phim đang chiếu
│   │   └── Privacy.cshtml             # Tuyên bố miễn trừ trách nhiệm học thuật MIS
│   ├── Movies/
│   │   ├── Index.cshtml               # Danh sách phim công khai
│   │   └── Details.cshtml             # Chi tiết phim, trailer, lịch chiếu theo ngày
│   ├── Showtimes/
│   │   └── Seats.cshtml               # Sơ đồ ghế trực quan, bảng giá, chú thích BE-01
│   ├── Account/
│   │   ├── Login.cshtml               # Form đăng nhập, gợi ý tài khoản demo
│   │   └── Profile.cshtml             # Thông tin tài khoản, email, danh sách vai trò
│   └── Shared/
│       ├── _Layout.cshtml             # Khung giao diện chính (Bootstrap 5, Responsive)
│       ├── AccessDenied.cshtml        # Thông báo 403 Forbidden
│       └── Error.cshtml               # Trang lỗi chuẩn hóa có Correlation ID
├── wwwroot/
│   ├── css/
│   │   └── site.css                   # Định kiểu rạp chiếu phim, màu sắc ghế theo hạng
│   └── js/
│       └── site.js                    # Script bổ trợ giao diện
├── Program.cs                         # Điểm khởi động ứng dụng, DI container, Pipeline
├── appsettings.json                   # Cấu hình logging, connection strings
└── BetaGiaiPhong.Web.csproj           # Cấu hình project .NET 10 MVC
```

---

## 4. Chi Tiết Triển Khai Kỹ Thuật

### 4.1. Xác Thực & Phân Quyền Không Xâm Lấn
- **Cơ chế:** Sử dụng `CookieAuthenticationDefaults.AuthenticationScheme` kết hợp `PasswordHasher<AppUser>`.
- **Quy trình xác thực:**
  1. Chuẩn hóa email đầu vào (`ToUpperInvariant()`).
  2. Truy vấn người dùng từ `dbo.Users` kèm các vai trò trong `dbo.UserRoles` và `dbo.Roles`.
  3. Kiểm tra trạng thái tài khoản: Nếu khác `ACTIVE` (ví dụ `DISABLED`), ghi log cảnh báo và từ chối đăng nhập với thông báo trung tính.
  4. Xác minh mật khẩu với hàm băm PBKDF2 hiện có.
  5. Nếu mật khẩu sai, trả về lỗi trung tính `"Email hoặc mật khẩu không chính xác."` nhằm triệt tiêu nguy cơ User Enumeration Attack.
  6. Khởi tạo `ClaimsPrincipal` với các claim chuẩn (`NameIdentifier`, `Email`, `Name`) và `ClaimTypes.Role` cho từng vai trò của người dùng.
  7. Thiết lập Cookie với các thuộc tính an toàn: `HttpOnly = true`, `SameSite = SameSiteMode.Lax`, `ExpireTimeSpan = TimeSpan.FromHours(8)`, `SlidingExpiration = true`.
- **Xử lý chuyển hướng & API Challenge:**
  - Với request MVC thông thường: Tự động chuyển hướng về `/account/login` khi chưa đăng nhập (401), hoặc `/account/access-denied` khi không đủ quyền (403).
  - Với request API (`/api/*`): Ghi đè sự kiện `OnRedirectToLogin` và `OnRedirectToAccessDenied` để trả về trực tiếp mã HTTP `401 Unauthorized` hoặc `403 Forbidden` thay vì chuyển hướng HTML.
- **Chống brute-force:** Áp dụng `LoginRateLimit` middleware (Fixed Window: 10 requests / 1 phút / IP).

### 4.2. Danh Mục Phim & Suất Chiếu
- **Lọc phim:** Chỉ các phim có `status = 'PUBLISHED'` mới được xuất hiện trên trang chủ, danh sách phim và API `/api/movies`. Phim ở trạng thái `DRAFT` hoặc `ARCHIVED` bị loại bỏ triệt để.
- **Xử lý ngày giờ:** Thời gian lưu trong cơ sở dữ liệu là UTC (`StartUtc`, `EndUtc`). Service tính toán chuyển đổi sang giờ Việt Nam (`SE Asia Standard Time`, UTC+7) để nhóm suất chiếu theo ngày (`dd/MM/yyyy`) và hiển thị giờ chiếu rõ ràng cho khán giả.

### 4.3. Sơ Đồ Ghế Chỉ Đọc (Read-Only Seat Map)
- **Nguyên tắc thiết kế:** Tuân thủ tuyệt đối giới hạn BE-01 — không tạo đơn hàng, không giữ chỗ, không mutate cơ sở dữ liệu qua HTTP GET.
- **Tính toán trạng thái ghế:**
  1. Kiểm tra toàn vẹn: Suất chiếu phải tồn tại, đang ở trạng thái `SCHEDULED`, và `room_id` phải khớp với phòng chiếu.
  2. Nạp toàn bộ danh mục ghế vật lý (`dbo.Seats`) của phòng chiếu và trạng thái tồn kho theo suất chiếu (`dbo.ShowtimeSeats`).
  3. Nhận diện trạng thái hiển thị:
     - Ghế không có trong `dbo.ShowtimeSeats` hoặc `status == 'BLOCKED'`: Trạng thái `BLOCKED`.
     - Ghế có `status == 'SOLD'`: Trạng thái `SOLD`.
     - Ghế có `status == 'RESERVED'`: Nếu thời gian giữ chỗ còn hiệu lực (`lock_expires_utc > now`), đánh dấu `HELD`. Nếu đã hết hạn (`lock_expires_utc <= now`), hiển thị là `AVAILABLE` (mô phỏng cơ chế giải phóng chỗ tự động mà không cần chạy batch background).
     - Ghế có `status == 'AVAILABLE'`: Trạng thái `AVAILABLE`.
  4. Ghép nối đơn giá tương ứng từ bảng `dbo.SeatPrices` dựa trên `seat_type_code` của từng ghế (`STANDARD`, `VIP`, `COUPLE`).

### 4.4. Phòng Thủ Bảo Mật & Xử Lý Lỗi
- **Anti-CSRF:** Mọi form HTML POST (`/account/login`, `/account/logout`) đều được bảo vệ bởi `@Html.AntiForgeryToken()` và kiểm tra tự động qua filter chống giả mạo token.
- **Security Headers:**
  - `X-Content-Type-Options: nosniff`
  - `X-Frame-Options: DENY`
  - `Referrer-Policy: strict-origin-when-cross-origin`
- **Correlation ID Middleware:**
  - Tự động kiểm tra hoặc sinh mới một chuỗi GUID duy nhất cho mỗi HTTP request.
  - Gắn vào `HttpContext.TraceIdentifier` và trả về trong header phản hồi `X-Correlation-ID`.
- **Global Exception Handler Middleware:**
  - Bắt toàn bộ unhandled exception trong pipeline.
  - Phân loại: Nếu request thuộc đường dẫn `/api/*`, trả về cấu trúc lỗi chuẩn RFC 7807 `ProblemDetails` định dạng JSON.
  - Các lỗi nghiệp vụ `BusinessRuleException` được ánh xạ về mã HTTP tương ứng (400, 404, 409, 422).
  - Lỗi hệ thống không xác định trả về mã 500 với thông điệp an toàn đã được khử trùng, kèm Correlation ID để tra cứu log, tuyệt đối không rò rỉ stack trace ra môi trường sản xuất.

---

## 5. Kết Quả Kiểm Thử Toàn Diện (Test Execution Evidence)

Hệ thống đã thực hiện kiểm thử hồi quy toàn bộ solution với kết quả tuyệt đối:
- **Tổng số test:** 46
- **Passed:** 46 (100%)
- **Failed:** 0
- **Thời gian chạy:** ~18 giây trên SQL Server LocalDB

### 5.1. Bảng Chi Tiết 18 Test Mới (Web Layer)

| Mã Test Case | Nhóm Kiểm Thử | Mục Tiêu & Mô Tả Kịch Bản | Kết Quả |
| :--- | :--- | :--- | :--- |
| `TC_WEB_AUTH_01` | Xác thực | Đăng nhập hợp lệ với tài khoản CUSTOMER, kiểm tra Cookie xác thực được thiết lập và chuyển hướng về trang chủ. | **PASS** |
| `TC_WEB_AUTH_02` | Xác thực | Đăng nhập với mật khẩu sai, xác nhận không cấp Cookie và hiển thị thông báo lỗi trung tính. | **PASS** |
| `TC_WEB_AUTH_03` | Xác thực | Đăng nhập với email không tồn tại, xác nhận thông báo lỗi giống hệt khi sai mật khẩu (chống User Enumeration). | **PASS** |
| `TC_WEB_AUTH_04` | Xác thực | Đăng nhập với tài khoản bị khóa (`status = 'DISABLED'`), xác nhận từ chối đăng nhập an toàn. | **PASS** |
| `TC_WEB_AUTH_05` | Phân quyền | Người dùng chưa đăng nhập truy cập `/account/profile`, xác nhận tự động chuyển hướng về `/account/login`. | **PASS** |
| `TC_WEB_AUTH_06` | Phân quyền | Request chưa xác thực gọi `GET /api/me`, xác nhận trả về mã `401 Unauthorized` dạng JSON (không chuyển hướng HTML). | **PASS** |
| `TC_WEB_AUTH_07` | Phân quyền | Phiên đăng nhập hợp lệ truy cập thành công cả trang HTML `/account/profile` và REST API `GET /api/me`. | **PASS** |
| `TC_WEB_CAT_01` | Danh mục | Truy cập trang chủ `/`, xác nhận HTTP 200, hiển thị Hero banner, danh sách phim và footer học thuật. | **PASS** |
| `TC_WEB_CAT_02` | Danh mục | Truy cập trang `/movies`, kiểm tra danh sách chỉ chứa phim `PUBLISHED`, không hiển thị phim nháp. | **PASS** |
| `TC_WEB_CAT_03` | Danh mục | Truy cập trang chi tiết phim `/movies/{id}`, xác nhận hiển thị thông tin phim và các suất chiếu gom theo ngày. | **PASS** |
| `TC_WEB_CAT_04` | Sơ đồ ghế | Truy cập `/showtimes/{id}/seats`, kiểm tra sơ đồ ghế hiển thị màn hình chiếu, các hàng ghế và bảng giá vé. | **PASS** |
| `TC_WEB_CAT_05` | REST API | Gọi `GET /api/movies`, xác nhận trả về JSON danh sách phim đã xuất bản với đầy đủ metadata. | **PASS** |
| `TC_WEB_CAT_06` | REST API | Gọi `GET /api/showtimes/{id}/seats`, xác nhận tính toán chính xác trạng thái ghế `HELD` và `AVAILABLE` khi có ghế hết hạn giữ chỗ. | **PASS** |
| `TC_WEB_SEC_01` | Bảo mật | Kiểm tra response của Web chứa đầy đủ các HTTP Security Headers bắt buộc (`nosniff`, `DENY`, `strict-origin-when-cross-origin`). | **PASS** |
| `TC_WEB_SEC_02` | Bảo mật | Gửi POST request không kèm Anti-CSRF token, xác nhận bị từ chối với mã HTTP lỗi phù hợp. | **PASS** |
| `TC_WEB_SEC_03` | Bảo mật | Gọi endpoint phát sinh lỗi unhandled, xác nhận trả về JSON RFC 7807 `ProblemDetails` mã 500 không rò rỉ stack trace. | **PASS** |
| `TC_WEB_SEC_04` | Bảo mật | Kiểm tra Middleware Correlation ID tự động sinh hoặc kế thừa header `X-Correlation-ID`. | **PASS** |
| `TC_WEB_SEC_05` | Bảo mật | Gọi API tới tài nguyên không tồn tại (`GET /api/movies/999999`), xác nhận trả về `ProblemDetails` mã `404 Not Found`. | **PASS** |

### 5.2. Tổng Hợp 28 Test Data Layer Hiện Có (Đều PASS)
- **Unit Cryptography Tests:** 6 tests (HMAC-SHA256 signature, AES-256-GCM token roundtrip, tampered ciphertext rejection).
- **Navigation Property Mapping:** 1 test (kiểm tra đầy đủ 22 quan hệ foreign key trong ModelBuilder).
- **Database Integrity & Reporting:** 2 tests (5 truy vấn toàn vẹn đa bảng 0 vi phạm; LINQ aggregation trên view `dbo.vw_ShowtimeSales`).
- **Concurrency & Race Conditions:** 6 tests (kiểm tra tranh chấp đồng thời 2 luồng, 10 luồng, 50 luồng; đặt chỗ phân tách; nguyên tử All-or-Nothing; giải phóng giữ chỗ quá hạn).
- **Payment & Webhook Workflows:** 6 tests (thanh toán thành công phát hành vé, hủy ghế khi thất bại, xử lý idempotent webhook trùng lặp, xử lý thanh toán muộn chuyển sang `NEEDS_REVIEW`).
- **QR Scanning Scenarios:** 6 tests (quét vé hợp lệ, từ chối vé đã dùng, tranh chấp quét đồng thời, từ chối vé giả mạo, từ chối sai suất chiếu, từ chối ngoài khung giờ).

---

## 6. Kiểm Tra Toàn Vẹn Mã Nguồn & Bảo Mật Hệ Thống

1. **Kiểm tra biên dịch Release:**
   ```powershell
   dotnet build .\BetaGiaiPhong.slnx -c Release
   # Kết quả: Build succeeded. 0 Warning(s), 0 Error(s).
   ```
2. **Kiểm tra trôi schema (EF Core Model Drift):**
   ```powershell
   dotnet ef migrations has-pending-model-changes --project .\src\BetaGiaiPhong.Data --startup-project .\src\BetaGiaiPhong.Data
   # Kết quả: No changes have been made to the model since the last migration.
   ```
3. **Quét lỗ hổng gói phụ thuộc (NuGet Vulnerabilities):**
   ```powershell
   dotnet list package --vulnerable --include-transitive
   # Kết quả: The given projects have no vulnerable packages given the current sources. (0 vulnerabilities).
   ```
4. **Kiểm tra bất biến schema tĩnh:**
   ```powershell
   python .\scripts\static_schema_checks.py
   # Kết quả: PASS (17 tables, 116 columns, 22 foreign keys verified).
   ```

---

## 7. Đánh Giá Mức Độ Sẵn Sàng Cho Giai Đoạn BE-02

Dự án đã hoàn thành trọn vẹn và vững chắc các mục tiêu của giai đoạn **BE-01**. Cơ sở hạ tầng hiện tại hoàn toàn sẵn sàng để bước sang **BE-02 (Seat Booking, Checkout & Order Lifecycle Management)**:

| Tiêu Chí Sẵn Sàng | Trạng Thái | Đánh Giá Chi Tiết |
| :--- | :--- | :--- |
| **Host ASP.NET Core** | **SẴN SÀNG** | Đã hoạt động ổn định, cấu hình DI, routing, middleware và pipeline hoàn chỉnh. |
| **Xác thực & Phiên làm việc** | **SẴN SÀNG** | Đã có cookie auth, phân biệt được khách hàng và nhân viên, quản lý claims chặt chẽ. |
| **Tra cứu danh mục & Ghế** | **SẴN SÀNG** | Catalog và Seat Map API cung cấp đầy đủ dữ liệu thời gian thực cho màn hình đặt vé. |
| **Dịch vụ nghiệp vụ lõi** | **SẴN SÀNG** | `SeatReservationService` và `PaymentService` đã có sẵn trong Data Layer và đã qua kiểm thử stress-concurrency. |
| **Toàn vẹn cơ sở dữ liệu** | **SẴN SÀNG** | 0 model drift, 0 pending migrations, schema SQL Server đồng bộ tuyệt đối. |
| **Quy trình kiểm thử** | **SẴN SÀNG** | `CustomWebApplicationFactory` đã được chuẩn hóa, sẵn sàng mở rộng cho các bài test POST HTTP booking và checkout. |

---
*Báo cáo được phê duyệt và lưu trữ tại `docs/quality/BE_01_REPORT.md`.*