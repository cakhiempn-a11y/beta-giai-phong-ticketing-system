# BÁO CÁO ĐÁNH GIÁ MỨC ĐỘ SẴN SÀNG PHÁT TRIỂN BACKEND (BACKEND READINESS)

**Dự án:** Phân tích, thiết kế và xây dựng hệ thống quản lý bán vé trực tuyến tại rạp chiếu phim Beta Giải Phóng  
**Mục tiêu:** Đánh giá tính khả thi, khả năng tái sử dụng và xác định các thành phần cần điều chỉnh/xây dựng mới khi chuyển sang phát triển ứng dụng Backend ASP.NET Core MVC / Web API.  
**Thời điểm lập:** 31/10/2026  

---

## 1. MA TRẬN ĐÁNH GIÁ MỨC ĐỘ SẴN SÀNG CỦA CÁC THÀNH PHẦN

Hội đồng kiểm định phân loại từng thành phần theo 4 mức độ:
- **READY (Sẵn sàng):** Có thể tái sử dụng trực tiếp trong ASP.NET Core mà không cần sửa đổi lớn.
- **NEEDS REFACTOR (Cần điều chỉnh):** Logic cốt lõi đã tốt nhưng cần điều chỉnh để tối ưu cho môi trường Web.
- **MISSING (Chưa có):** Chưa tồn tại trong mã nguồn hiện tại, cần xây dựng mới ở tầng Backend.
- **BLOCKER (Chặn đứng):** Lỗi nghiêm trọng ngăn cản ứng dụng khởi động hoặc vận hành an toàn.

| Thành phần kỹ thuật | Trạng thái | Đánh giá chi tiết | Hướng xử lý khi chuyển sang Backend |
|---|:---:|---|---|
| **1. Entities & Data Models** | **NEEDS REFACTOR** | 17 Entity class đã định nghĩa chuẩn xác 116 thuộc tính, nhưng thiếu toàn bộ Navigation Properties. | Bổ sung các thuộc tính điều hướng (`public Movie Movie { get; set; }`, `public ICollection<Seat> Seats { get; set; }`) vào Entity và cập nhật cấu hình Fluent API để hỗ trợ `.Include()` trong LINQ. |
| **2. DbContext & Model Configuration** | **READY** | `BetaDbContext` và `BetaModelConfiguration` cấu hình đầy đủ bảng, schema `dbo`, khóa chính, khóa ngoại ghép, check constraints và filtered index. | Tái sử dụng trực tiếp trong Backend. Bổ sung cấu hình bỏ qua `PendingModelChangesWarning` và khai báo Keyless Entity cho view `vw_ShowtimeSales`. |
| **3. Dependency Injection & Service Registration** | **READY** | Tệp [ServiceRegistration.cs](file:///d:/Downloads/BGP_EFCore10_SQLServer_TrienKhai_FullPackage_v1_0/BetaGiaiPhong_EFCore_SQLServer_v1_0/src/BetaGiaiPhong.Data/ServiceRegistration.cs#L8-L24) đã cung cấp sẵn phương thức mở rộng `services.AddBetaCinemaData(connection)` chuẩn mực theo khuyến nghị của Microsoft. | Tái sử dụng 100% trong `Program.cs` của ứng dụng ASP.NET Core. |
| **4. Service Lifetime (Vòng đời dịch vụ)** | **READY** | `BetaDbContext`: Scoped; `SystemClock`: Singleton; `QrTokenProtector`: Singleton; Các Domain Services (`ShowtimePublishingService`, `SeatReservationService`,...): Scoped. | Hoàn toàn tương thích và an toàn với mô hình Request-Response đa luồng của ASP.NET Core. |
| **5. Quản lý Giao dịch & Khóa đồng thời** | **READY** | Cơ chế pessimistic locking `WITH (UPDLOCK, HOLDLOCK)` kết hợp cấp độ cô lập `Serializable` đã được kiểm chứng chịu tải tốt với 50 request cạnh tranh mà không gây deadlock. | Sử dụng trực tiếp trong các Action Controller nghiệp vụ đặt vé và thanh toán. |
| **6. DTOs & ViewModel Boundaries** | **MISSING** | Chưa có các lớp DTO (Data Transfer Objects) và ViewModel để phân tách dữ liệu gửi/nhận qua giao diện và API. | Xây dựng thư mục `Models/DTOs` và `Models/ViewModels` (ví dụ: `ShowtimeSelectionDto`, `SeatLayoutViewModel`, `BookingRequestDto`, `PaymentCallbackDto`). Tuyệt đối không để View hoặc API client binding trực tiếp vào Entity. |
| **7. Tầng Kiểm tra hợp lệ dữ liệu (Validation)** | **MISSING** | Mới chỉ có validation ở mức database CHECK constraints và điều kiện trong domain service (`BusinessRuleException`). Chưa có validation ở tầng HTTP Request. | Triển khai thư viện `FluentValidation` hoặc ASP.NET Core `Data Annotations` để kiểm tra tính hợp lệ của Request DTOs ngay tại tầng Filter/Controller. |
| **8. Xử lý ngoại lệ toàn cục (Exception Handling)** | **MISSING** | Ứng dụng hiện ném `BusinessRuleException` trực tiếp, chưa có middleware chuyển đổi thành HTTP Status Code hoặc giao diện báo lỗi thân thiện. | Xây dựng `GlobalExceptionHandlerMiddleware` (hoặc `IExceptionHandler` trong .NET 10) để map tự động: `BusinessRuleException` $\rightarrow$ `HTTP 400/409/422`, `UnauthorizedAccessException` $\rightarrow$ `HTTP 401/403`, lỗi bất thường $\rightarrow$ `HTTP 500` kèm Correlation ID an toàn. |
| **9. Tích hợp Xác thực & Phân quyền (AuthN/AuthZ)** | **MISSING** | Có bảng `Users`, `Roles`, `UserRoles` và hàm `PasswordHasher<AppUser>`, nhưng chưa có Middleware Authentication/Authorization. | Cấu hình ASP.NET Core Cookie Authentication cho cổng Quản lý/Nhân viên (MVC) và JWT Bearer Authentication cho API Khách hàng (nếu có). Áp dụng các attribute `[Authorize(Roles = "MANAGER,ADMIN")]`, `[Authorize(Roles = "STAFF")]`. |
| **10. Controller Callback Thanh toán (Payment Webhook)** | **MISSING** | `PaymentService.ApplyVerifiedMockAsync` và `MockPaymentSecurity` đã sẵn sàng, nhưng chưa có Endpoint HTTP tiếp nhận callback từ cổng thanh toán. | Tạo `PaymentWebhookController` với action `POST /api/payments/mock/callback`, kiểm tra chữ ký HMAC và gọi `PaymentService` xử lý. |
| **11. Controller Soát vé QR (Ticket Scanning)** | **MISSING** | `TicketScanningService.ScanAsync` đã sẵn sàng, chưa có giao diện web hoặc endpoint API cho nhân viên soát vé quét mã. | Tạo `TicketScanController` (giao diện MVC quét camera / nhập token hoặc API JSON) có xác thực quyền `STAFF`. |
| **12. Dịch vụ Thống kê Báo cáo (Reporting)** | **NEEDS REFACTOR** | View `vw_ShowtimeSales` đã có trong SQL Server, tính toán chính xác doanh thu và tỷ lệ lấp đầy, nhưng chưa có Entity tương ứng trong C# để truy vấn qua LINQ. | Khai báo Keyless Entity `ShowtimeSalesSummary` trong DbContext và tạo `ReportController` hiển thị biểu đồ/bảng số liệu doanh thu cho Quản lý. |
| **13. Tác vụ ngầm dọn dẹp đơn hết hạn (Background Worker)** | **READY (Logic) / MISSING (Worker)** | Phương thức `OrderLifecycleService.ExpireDueAsync()` đã sẵn sàng, nhưng cần một BackgroundService để chạy định kỳ. | Triển khai `IHostedService` / `BackgroundService` trong ASP.NET Core để gọi `ExpireDueAsync()` định kỳ mỗi 60 giây. |
| **14. Dự án Kiểm thử Tự động (Testing)** | **MISSING** | Chưa có project `tests` độc lập sử dụng framework xUnit/NUnit trong solution. | Khởi tạo project `BetaGiaiPhong.Tests` sử dụng `xUnit` và `Microsoft.AspNetCore.Mvc.Testing` (`WebApplicationFactory`). |

---

## 2. KIẾN TRÚC BACKEND ĐỀ XUẤT CHO DỰ ÁN

Để đảm bảo tính khả thi cao, dễ bảo trì, dễ demo bảo vệ đồ án MIS mà **không bị sa đà vào việc chia quá nhiều tầng phức tạp (over-engineering) hay kiến trúc Microservices không cần thiết**, kiến trúc đề xuất cho Backend là **Mô hình 3 tầng cải tiến (Clean Modular Monolith)**:

```text
BetaGiaiPhong.slnx
│
├── src/
│   ├── BetaGiaiPhong.Data/               [TẦNG TRUY CẬP DỮ LIỆU - DATA ACCESS LAYER]
│   │   ├── Entities/                     (17 Entity classes - Bổ sung Navigation Properties)
│   │   ├── Persistence/                  (BetaDbContext, BetaModelConfiguration, Migrations)
│   │   └── Services/                     (ShowtimePublishing, SeatReservation, Payment, Scanning, Shared)
│   │
│   ├── BetaGiaiPhong.Web/                [TẦNG ỨNG DỤNG & GIAO DIỆN - ASP.NET CORE MVC & API]
│   │   ├── Controllers/                  (HomeController, BookingController, PaymentController, AdminController, ScanController)
│   │   ├── Models/
│   │   │   ├── DTOs/                     (Request/Response DTOs cho API và Ajax)
│   │   │   └── ViewModels/               (Dữ liệu render giao diện Razor Views: MovieList, SeatMap, OrderSummary)
│   │   ├── Views/                        (Giao diện đặt vé khách hàng, giao diện quản lý rạp, giao diện soát vé)
│   │   ├── Middlewares/                  (GlobalExceptionHandlerMiddleware, RequestLoggingMiddleware)
│   │   ├── BackgroundWorkers/            (OrderExpiryBackgroundWorker kế thừa BackgroundService)
│   │   ├── appsettings.json              (Cấu hình chuỗi kết nối và các tham số vận hành)
│   │   └── Program.cs                    (Cấu hình DI, Authentication, Middleware Pipeline)
│   │
│   └── BetaGiaiPhong.Runner/             [CÔNG CỤ QUẢN TRỊ & HẠT GIỐNG DỮ LIỆU - CONSOLE RUNNER]
│       ├── Program.cs                    (Migration CLI, Seed Demo Data, Integrity Checks)
│       └── DemoSeeder.cs
│
└── tests/
    └── BetaGiaiPhong.Tests/              [KIỂM THỬ TỰ ĐỘNG - XUNIT TEST PROJECT]
        ├── UnitTests/                    (Kiểm thử độc lập các dịch vụ tính giá, kiểm tra trùng lịch)
        └── IntegrationTests/             (Kiểm thử tích hợp Controller và CSDL bằng WebApplicationFactory)
```

---

## 3. LỘ TRÌNH TRIỂN KHAI BACKEND TỪNG BƯỚC (ROADMAP)

### Bước 1: Chuẩn bị và Sửa lỗi Data Layer (Ưu tiên P1 & P2)
1. Thêm `.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))` vào DbContextOptions trong `Program.cs` của Runner và Backend.
2. Thêm tham chiếu package `System.Security.Cryptography.Pkcs` bản mới nhất vào `BetaGiaiPhong.Data.csproj` để triệt tiêu cảnh báo bảo mật `NU1903`.
3. Sửa hàm sinh chuỗi ngẫu nhiên trong `scripts/01_prepare_dev_session.ps1` để chạy được trên PowerShell 5.1.
4. Bổ sung Navigation Properties vào 17 Entity class và tinh chỉnh cấu hình `BetaModelConfiguration.cs`.
5. Tạo Entity Type cho view `vw_ShowtimeSales` trong `BetaDbContext`.

### Bước 2: Khởi tạo Project Backend `BetaGiaiPhong.Web`
1. Chạy lệnh: `dotnet new mvc -n BetaGiaiPhong.Web -o src/BetaGiaiPhong.Web --framework net10.0`.
2. Thêm project vào solution: `dotnet sln add src/BetaGiaiPhong.Web/BetaGiaiPhong.Web.csproj`.
3. Tham chiếu sang Data Layer: `dotnet add src/BetaGiaiPhong.Web/BetaGiaiPhong.Web.csproj reference src/BetaGiaiPhong.Data/BetaGiaiPhong.Data.csproj`.
4. Đăng ký DI trong `Program.cs`: `builder.Services.AddBetaCinemaData(connectionString)`.

### Bước 3: Triển khai các Chức năng Nghiệp vụ Cốt lõi
1. **Phân hệ Khách hàng (Customer Web Portal):**
   - Trang chủ: Danh sách phim đang chiếu / sắp chiếu (`MoviesController`).
   - Chọn suất chiếu & Sơ đồ ghế trực quan (`BookingController.Showtime`, `SeatMap`).
   - Giữ ghế đồng thời & Tạo đơn hàng (`SeatReservationService`).
   - Trang thanh toán & Giả lập thanh toán (`PaymentController`).
   - Trang xác nhận đơn hàng thành công & Hiển thị mã QR vé (`BookingController.Confirmation`).
2. **Phân hệ Nhân viên soát vé (Staff QR Scanning Portal):**
   - Màn hình quét QR (hỗ trợ nhập mã hoặc camera) kết nối `TicketScanningService`.
3. **Phân hệ Quản lý rạp (Manager Portal):**
   - Quản lý lịch chiếu phim (kiểm tra trùng lịch và buffer tự động qua `ShowtimePublishingService`).
   - Báo cáo doanh thu & Tỷ lệ lấp đầy suất chiếu (truy vấn từ view `vw_ShowtimeSales`).
4. **Tác vụ nền (Background Worker):**
   - Quét và giải phóng ghế của các đơn hàng hết hạn thanh toán mỗi phút.

### Bước 4: Kiểm thử và Đóng gói Bảo vệ Đồ án
1. Tạo project `BetaGiaiPhong.Tests` và viết bộ test tự động cho Controller và Service.
2. Kiểm tra giao diện người dùng trên trình duyệt (Responsive, trải nghiệm chọn ghế mượt mà).
3. Đóng gói mã nguồn và chuẩn bị kịch bản demo trực tiếp (Demo giữ ghế tranh chấp, demo quét QR, demo xem báo cáo doanh thu).
