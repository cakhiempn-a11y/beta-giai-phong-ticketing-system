# Báo Cáo Kỹ Thuật Giai Đoạn BE-03
## Thanh Toán Giả Lập, Webhook Xác Thực, Vé Điện Tử & Hiển Thị Mã QR
### Báo Cáo Khắc Phục Lỗ Hổng Bảo Mật, Kiểm Tra Kỹ Thuật Toàn Diện & Trạng Thái Sẵn Sàng Hợp Nhất (Ready to Merge)

**Dự án:** Phân tích, thiết kế và xây dựng hệ thống quản lý bán vé trực tuyến tại rạp chiếu phim Beta Giải Phóng  
**Bối cảnh học thuật:** Đề án tốt nghiệp Hệ thống thông tin quản lý (MIS) — Dữ liệu mô phỏng, nguyên mẫu học thuật (Academic Prototype), không liên kết với hệ thống thực tế của Beta Cinemas  
**Phiên bản:** BE-03 Final Hardening & Merge Readiness  
**Ngày hoàn thành:** 31/10/2026  
**Nhánh Git:** `feat/mock-payment-ticketing`  
**Tài khoản GitHub cam kết:** `cakhiempn-a11y` (`320607805+cakhiempn-a11y@users.noreply.github.com`)  
**Trạng thái nghiệm thu:** HOÀN THÀNH TOÀN DIỆN (104/104 Tests Passed, 0 Vulnerabilities, 0 Model Drift, Release Build 0 Error/0 Warning, 6/6 Multi-Table Invariants Passed) — **READY TO MERGE**

---

## 1. Tóm Tắt Thực Thi (Executive Summary)

Giai đoạn **BE-03** đã hoàn thành toàn bộ chức năng và được rà soát, củng cố bảo mật chuyên sâu (Final Hardening) cho hệ thống **thanh toán giả lập (Mock Payment), webhook xác thực HMAC-SHA256, phát hành vé điện tử và hiển thị mã QR bảo mật**.

### Tuyên Bố Nguyên Mẫu Học Thuật (Academic Prototype Disclaimer):
> Đây là sản phẩm nguyên mẫu học thuật phục vụ đồ án tốt nghiệp MIS. Hệ thống sử dụng dữ liệu mô phỏng (synthetic data), hợp đồng mật mã học giả lập và **KHÔNG kết nối tới bất kỳ cổng thanh toán thương mại thực tế nào (VNPAY, MoMo, ZaloPay, Stripe)** hay thiết bị phần cứng cửa soát vé thực tế tại rạp.

### Kết quả củng cố bảo mật và chất lượng nổi bật:
1. **Loại Bỏ Hoàn Toàn Bí Mật Khỏi Kho Mã Nguồn (Secrets Remediation):**
   - Đã loại bỏ triệt để các khóa cố định `BGP_MOCK_HMAC_KEY_B64` và `BGP_QR_AES_KEY_B64` khỏi `src/BetaGiaiPhong.Web/appsettings.Development.json` và `src/BetaGiaiPhong.Web/Properties/launchSettings.json`.
   - Sinh 2 khóa CSPRNG 256-bit (32 byte) độc lập, lưu trữ cục bộ qua công cụ chuẩn **.NET User Secrets** (`beta-giai-phong-web-8c9f1a2b`), không lưu vết vào Git.
   - Đưa cờ `BGP_ENABLE_MOCK_PAYMENT_SIMULATOR` về trạng thái mặc định tắt (`false` / không cấu hình trong repo); chỉ kích hoạt cục bộ qua User Secrets khi lập trình viên cần kiểm thử.
   - Bổ sung kiểm tra nghiêm ngặt lúc khởi động (`Program.cs`): Bắt buộc khóa AES phải đúng 32 byte Base64, khóa HMAC tối thiểu 32 byte Base64; nếu thiếu hoặc sai độ dài sẽ dừng ứng dụng ngay lập tức (fail-closed) với thông báo tiếng Việt rõ ràng.

2. **Kiểm Soát Quyền Truy Cập Mã QR Theo Nguyên Tắc Fail-Closed (Strict QR Access Control):**
   - Đảm bảo endpoint `/tickets/{ticketId}/qr` chỉ cho phép khách hàng chủ sở hữu vé tải ảnh QR (`[Authorize(Roles = "CUSTOMER")]`). Nhân viên nội bộ (`STAFF`) bị từ chối truy cập với HTTP 403 Forbidden.
   - Khách vãng lai chưa đăng nhập bị từ chối với HTTP 401 Unauthorized (xử lý chuyển đổi trực tiếp trong cookie middleware thay vì redirect 302).
   - Truy cập chéo vé giữa các khách hàng hoặc vé không tồn tại đều phản hồi HTTP 404 Not Found, không làm lộ sự tồn tại của vé (chống IDOR enumeration).
   - Chỉ cho phép tải mã QR khi đơn hàng sở hữu đã ở trạng thái `PAID` và vé ở trạng thái hợp lệ `ISSUED` hoặc `USED`. Đơn hàng chưa thanh toán trả về 404 Not Found.
   - Thiết lập header HTTP chống lưu bộ đệm bắt buộc: `Cache-Control: no-store, no-cache, must-revalidate` và `Pragma: no-cache`.

3. **Hoàn Thiện Hợp Đồng REST API Thanh Toán:**
   - Triển khai endpoint `GET /api/orders/{id}/payments/{paymentId}` trong `PaymentsApiController.cs` với đầy đủ kiểm tra quyền sở hữu của khách hàng (404 khi truy cập IDOR).
   - Cập nhật API `POST /api/orders/{id}/payments/mock-attempt` trả về HTTP 201 Created kèm header `Location` trỏ chính xác tới endpoint chi tiết thanh toán `GET /api/orders/{id}/payments/{paymentId}`.

4. **Giải Mã & Kiểm Thử Mã QR Thuần C# (Pure C# QR Verification):**
   - Xây dựng lớp giải mã ảnh QR độc lập `QrPngDecoder.cs` sử dụng `System.IO.Compression.ZLibStream` (.NET 10) và `ZXing.Net` core mà **không phụ thuộc vào thư viện ImageSharp trung gian có lỗ hổng bảo mật** (giữ nguyên 0 lỗ hổng bảo mật NuGet).
   - Thực hiện kiểm thử toàn trình (End-to-End): Tải mảng byte ảnh PNG trực tiếp từ endpoint HTTP, giải mã ra token thô dạng Base64URL, băm SHA-256 đối chiếu trùng khớp 100% với `dbo.Tickets.qr_hash`, và đưa qua toàn bộ vòng đời soát vé với `TicketScanningService` (`ACCEPTED` -> `ALREADY_USED` -> `WRONG_SHOWTIME` -> `OUTSIDE_WINDOW`).
   - Kiểm chứng thành công khả năng giải mã token khi tiến trình khởi động lại với cùng khóa bí mật (multi-instance / process restart resilience) và từ chối khi dùng sai khóa (`CryptographicException`).

5. **Bộ Kiểm Thử & Bất Biến Toàn Vẹn Đạt 100%:**
   - Toàn bộ solution đạt **104/104 Tests Passed** (gồm 93 test methods độc lập và 104 test execution instances).
   - Đạt 6/6 kiểm tra bất biến dữ liệu đa bảng qua `BetaGiaiPhong.Runner verify` với 0 vi phạm.

---

## 2. Thông Số Kỹ Thuật Mật Mã Học & Ràng Buộc Dữ Liệu (Cryptographic Specifications)

Hệ thống tuân thủ chặt chẽ các kiểu dữ liệu và định dạng chuỗi chuẩn hóa đã được định nghĩa tại tầng dữ liệu:

1. **Chuỗi Ký Danh Định Chuẩn Cho Webhook HMAC-SHA256:**
   - Chuỗi ký chuẩn sử dụng dấu xuống dòng (`\n`) làm ký tự phân tách:
     ```csharp
     string.Join("\n", "MOCK", ProviderEventRef, AttemptKey, ProviderTransactionRef, OrderCode, AmountVnd, Outcome)
     ```
   - Chữ ký được gửi qua header `X-Signature-SHA256` (hoặc `X-Signature`) và xác thực bằng `CryptographicOperations.FixedTimeEquals` chống tấn công đo lường thời gian (Timing Attacks).

2. **Định Dạng Token & Khóa Bí Mật Mã QR:**
   - **Token Thô (Plaintext Token):** Chuỗi ngẫu nhiên 43 ký tự chuẩn Base64URL, sinh từ 32 byte CSPRNG (`Base64UrlEncode(RandomBytes(32))`), không chứa ký tự nhạy cảm URL. Tuyệt đối không bao giờ lưu văn bản rõ trong database.
   - **Băm Tìm Kiếm (Lookup Hash):** Cột `dbo.Tickets.qr_hash` kiểu dữ liệu `binary(32)` trong SQL Server (`byte[]` trong C# EF Core), là kết quả băm SHA-256 của chuỗi token Base64URL. Được đánh chỉ mục duy nhất có lọc (unique filtered index) để phục vụ soát vé với độ phức tạp $O(1)$.
   - **Bản Mã Lưu Trữ (Ciphertext Storage):** Cột `dbo.Tickets.qr_token_cipher` kiểu dữ liệu `varbinary(max)` trong SQL Server (`byte[]` trong C# EF Core), chứa mảng byte gồm: `12-byte Nonce + 16-byte Tag + Ciphertext` được mã hóa xác thực bằng thuật toán AES-256-GCM.
   - **Khóa Bí Mật:** `BGP_QR_AES_KEY_B64` và `BGP_MOCK_HMAC_KEY_B64` là các mảng byte 256-bit độc lập, mã hóa dạng Base64 (44 ký tự).

3. **Cơ Chế Khởi Tạo & Hết Hạn Payment Attempt:**
   - `PaymentService.CreateMockAttemptAsync` kiểm tra `order.ExpiresUtc <= clock.UtcNow`. Nếu đã hết hạn giữ ghế, phương thức ném ngoại lệ `ORDER_UNAVAILABLE` mà **không trực tiếp thay đổi trạng thái đơn hàng sang EXPIRED** trong transaction này. Việc chuyển trạng thái và giải phóng ghế được thực hiện độc lập bởi `OrderLifecycleService.ExpireDueAsync` hoặc qua luồng kiểm tra checkout.

4. **Chiến Lược Xoay Khóa & Lưu Giữ Dữ Liệu Demo (Key Rotation & Data Retention):**
   - Trong môi trường phát triển hiện tại, bảng `dbo.Tickets` trên database `BetaGiaiPhong_Dev` có 0 bản ghi vé thực tồn, do đó không xảy ra xung đột dữ liệu cũ.
   - **Quy trình xoay khóa có kiểm soát khi cần bảo toàn dữ liệu (Ciphertext Migration):**
     1. Nạp khóa cũ `Key_Old` và khóa mới `Key_New`.
     2. Đọc từng bản ghi vé điện tử: `token = QrTokenProtector.Unseal(ticket.QrTokenCipher, Key_Old)`.
     3. Mã hóa lại với khóa mới: `ticket.QrTokenCipher = QrTokenProtector.Seal(token, Key_New)`.
     4. Giá trị `ticket.QrTokenHash` (SHA-256) và token gốc hoàn toàn được bảo toàn, không làm hỏng vé đã in hay mã QR của khách hàng.

---

## 3. Danh Mục Tệp Thay Đổi Trong Giai Đoạn Hardening

| Đường dẫn tệp | Loại thay đổi | Chi tiết nội dung thay đổi |
| :--- | :--- | :--- |
| `src/BetaGiaiPhong.Web/appsettings.Development.json` | Bảo mật | Loại bỏ các khóa HMAC, AES và cờ simulator đã commit; chỉ giữ cấu hình Logging và Database. |
| `src/BetaGiaiPhong.Web/Properties/launchSettings.json` | Bảo mật | Loại bỏ hoàn toàn các biến môi trường khóa bí mật và simulator khỏi các profile `http` và `https`. |
| `src/BetaGiaiPhong.Web/BetaGiaiPhong.Web.csproj` | Cấu hình | Thêm `<UserSecretsId>beta-giai-phong-web-8c9f1a2b</UserSecretsId>` phục vụ quản lý khóa cục bộ an toàn. |
| `src/BetaGiaiPhong.Web/Program.cs` | Bảo mật / Logic | Thêm logic kiểm tra khóa lúc startup (bắt buộc đúng 32 byte Base64); cấu hình intercept 401/403 cho `/tickets/*/qr`. |
| `src/BetaGiaiPhong.Web/Services/ITicketQueryService.cs` | Kiến trúc | Bắt buộc `customerId` là kiểu `long` không nullable trong phương thức `GenerateQrCodePngBytesAsync`. |
| `src/BetaGiaiPhong.Web/Services/TicketQueryService.cs` | Bảo mật / Nghiệp vụ | Kiểm tra `customerId > 0`, kiểm tra đơn hàng thuộc khách và phải là `PAID`, vé phải là `ISSUED` hoặc `USED`. |
| `src/BetaGiaiPhong.Web/Controllers/TicketsController.cs` | Bảo mật | Gắn quyền `[Authorize(Roles = "CUSTOMER")]`, trả về `NotFound()` nếu `customerId <= 0` hoặc vé không hợp lệ. |
| `src/BetaGiaiPhong.Web/Api/TicketsApiController.cs` | Bảo mật | Kiểm tra đơn hàng bắt buộc phải là `PAID`, ngăn lộ vé của đơn chưa thanh toán. |
| `src/BetaGiaiPhong.Web/Api/PaymentsApiController.cs` | REST API | Triển khai `GET /api/orders/{id}/payments/{paymentId}` và trả về `CreatedAtAction` trong API `mock-attempt`. |
| `tests/BetaGiaiPhong.Tests/BetaGiaiPhong.Tests.csproj` | Phụ thuộc | Tham chiếu `ZXing.Net` core mà không dùng `ImageSharp` binding để tránh lỗ hổng bảo mật CVE. |
| `tests/BetaGiaiPhong.Tests/Helpers/QrPngDecoder.cs` | Kiểm thử | Triển khai bộ giải mã ảnh PNG 1-bit Grayscale thuần C# bằng `ZLibStream` và `ZXing.Net.QrCode`. |
| `tests/BetaGiaiPhong.Tests/Helpers/TestClock.cs` | Kiểm thử | Giả lập `ISystemClock` phục vụ kiểm tra khung giờ quét vé mà không sửa dữ liệu database. |
| `tests/BetaGiaiPhong.Tests/UnitTests/QrTokenProtectorTests.cs` | Kiểm thử | Thêm test giải mã ảnh PNG thực tế, test sống sót sau restart process và test từ chối sai khóa. |
| `tests/BetaGiaiPhong.Tests/WebTests/MockPaymentAndTicketWebTests.cs` | Kiểm thử | Nâng cấp và mở rộng bộ test từ 22 lên 26 test methods chuyên sâu, bao phủ toàn bộ các kịch bản bảo mật và quét QR. |

---

## 4. Danh Mục Ca Kiểm Thử Tích Hợp Web (`MockPaymentAndTicketWebTests`)

Toàn bộ 26 ca kiểm thử chuyên sâu được thực thi trên cơ sở dữ liệu SQL Server thực tế thông qua `CustomWebApplicationFactory`:

| Mã Test | Tên Phương Thức Kiểm Thử | Mục Tiêu Nghiệp Vụ & Ràng Buộc Kiểm Chứng | Kết Quả |
| :--- | :--- | :--- | :--- |
| **TC_WEB_PAY_01** | `CreateMockAttempt_ValidPendingOrder_ReturnsAttemptDetails` | Tạo payment attempt thành công cho đơn PENDING_PAYMENT, trả về 201 Created. | **PASS** |
| **TC_WEB_PAY_02** | `CreateMockAttempt_Unauthenticated_Returns401` | Khách vãng lai gọi attempt API bị từ chối 401 Unauthorized. | **PASS** |
| **TC_WEB_PAY_03** | `CreateMockAttempt_OtherCustomerOrder_Returns404` | Khách A gọi attempt đơn của Khách B trả về 404 Not Found (chống IDOR). | **PASS** |
| **TC_WEB_PAY_04** | `CreateMockAttempt_NonPendingOrder_Returns409Conflict` | Gọi attempt trên đơn không ở PENDING_PAYMENT trả về 409 Conflict. | **PASS** |
| **TC_WEB_PAY_04b** | `GetPaymentAttempt_Owner_Returns200_CrossCustomer_Returns404` | Kiểm tra REST API `GET .../payments/{paymentId}`: đúng chủ trả về 200, khách khác trả về 404, khách vãng lai 401. | **PASS** |
| **TC_WEB_PAY_05** | `CreateMockAttempt_ExpiredReservation_MarksExpired_Returns409` | Gọi attempt khi đơn hết hạn giữ ghế: trả về 409 Conflict. | **PASS** |
| **TC_WEB_PAY_06** | `Webhook_MissingSignatureHeader_Returns401Unauthorized` | Webhook thiếu header chữ ký bị từ chối 401 Unauthorized. | **PASS** |
| **TC_WEB_PAY_07** | `Webhook_InvalidSignature_Returns401Unauthorized` | Webhook có chữ ký sai bị từ chối 401 Unauthorized. | **PASS** |
| **TC_WEB_PAY_08** | `Webhook_TamperedAmount_Returns401Unauthorized` | Payload webhook bị can thiệp giá tiền bị từ chối 401 Unauthorized. | **PASS** |
| **TC_WEB_PAY_09** | `Webhook_ValidPayment_TransitionsToPAID_SeatsSold_TicketsIssued` | Webhook thanh toán thành công: đơn chuyển PAID, ghế chuyển SOLD, vé được tạo ISSUED. | **PASS** |
| **TC_WEB_PAY_10** | `Webhook_DuplicatePayload_Returns200Idempotent` | Webhook gửi lặp lại giao dịch: xử lý idempotent, trả về 200 OK và không cấp vé thừa. | **PASS** |
| **TC_WEB_PAY_11** | `Webhook_LatePaymentOnExpiredOrder_RecordsNeedsReview_NoTickets` | Thanh toán đến trễ trên đơn đã hết hạn: ghi nhận NEEDS_REVIEW, không cấp vé, giữ nguyên đơn EXPIRED. | **PASS** |
| **TC_WEB_PAY_12** | `Webhook_PaymentFailure_ReleasesSeatsAndMarksFailed` | Webhook báo thất bại: đơn chuyển FAILED, giải phóng ghế về AVAILABLE. | **PASS** |
| **TC_WEB_PAY_13** | `Webhook_ConcurrentCallbacks_MaintainsDataIntegrity` | 5 webhook đồng thời cùng 1 đơn: đúng 1 yêu cầu hoàn tất đơn, 4 yêu cầu còn lại idempotent. | **PASS** |
| **TC_WEB_PAY_14** | `CheckoutPage_RendersCountdownTimerAndMockDisclaimer` | Trang checkout hiển thị đồng hồ đếm ngược 5 phút và khuyến cáo học thuật rõ ràng. | **PASS** |
| **TC_WEB_PAY_15** | `CheckoutPage_ExpiredOrder_RedirectsToDetailsWithError` | Truy cập checkout khi đơn đã hết hạn: chuyển hướng về trang chi tiết kèm thông báo lỗi. | **PASS** |
| **TC_WEB_PAY_16** | `GetTickets_CustomerOrder_ReturnsTicketsWithVietnamTime` | Trang vé hiển thị đúng mã vé, phòng chiếu, ghế ngồi và giờ chiếu UTC+7. | **PASS** |
| **TC_WEB_PAY_17** | `GetQrCode_DownloadsRealPng_DecodesToken_VerifiesScanLifecycle` | Tải mảng byte ảnh PNG thực tế, giải mã qua `QrPngDecoder`, đối chiếu SHA-256 với database, kiểm tra chu trình soát vé ACCEPTED -> ALREADY_USED -> WRONG_SHOWTIME. | **PASS** |
| **TC_WEB_PAY_17b** | `GetQrCode_ScannedOutsideWindow_ReturnsOutsideWindow` | Quét vé ngoài khung giờ cho phép (sử dụng fake `TestClock`) trả về OUTSIDE_WINDOW mà không sửa database. | **PASS** |
| **TC_WEB_PAY_18** | `GetQrCode_CrossCustomerAccess_Returns404NotFound` | Khách A tải QR vé của Khách B bị từ chối 404 Not Found (chống IDOR). | **PASS** |
| **TC_WEB_PAY_19** | `GetQrCode_Unauthenticated_Returns401Unauthorized` | Khách vãng lai chưa đăng nhập tải QR bị trả về 401 Unauthorized trực tiếp. | **PASS** |
| **TC_WEB_PAY_20** | `GetQrCode_StaffRole_Returns403Forbidden` | Tài khoản nhân viên nội bộ (`STAFF`) cố tình tải ảnh QR của khách bị từ chối 403 Forbidden. | **PASS** |
| **TC_WEB_PAY_21** | `GetQrCode_UnpaidOrder_Returns404NotFound` | Đơn hàng chưa thanh toán (`PENDING_PAYMENT`) không thể tải mã QR, trả về 404 Not Found. | **PASS** |
| **TC_WEB_PAY_22** | `TicketApi_DoesNotLeakRawTokenOrCipher` | API vé JSON trả về URL ảnh QR, tuyệt đối không làm lộ token thô, cipher hay hash bí mật. | **PASS** |
| **TC_WEB_PAY_23** | `DemoPay_SimulatorDisabled_Returns404NotFound` | Khi simulator tắt: gọi endpoint demo-pay trả về HTTP 404 Not Found. | **PASS** |
| **TC_WEB_PAY_24** | `DemoPay_SimulatorEnabled_CustomerOrder_ExecutesPaymentFlow` | Khi simulator bật: khách gọi demo-pay hợp lệ được chuyển trạng thái PAID và redirect trang vé. | **PASS** |
| **TC_WEB_PAY_25** | `DemoPay_WithoutCsrfToken_Returns400BadRequest` | Gọi demo-pay thiếu anti-forgery token bị chặn với HTTP 400 Bad Request. | **PASS** |
| **TC_WEB_PAY_26** | `DatabaseInvariants_ZeroViolationsAfterPayments` | Xác minh toàn bộ các bất biến đa bảng trên SQL Server sau các luồng thanh toán: 0 vi phạm. | **PASS** |

---

## 5. Bằng Chứng Xác Minh Kỹ Thuật (Verification Evidence)

### 5.1. Kết Quả Kiểm Thử Toàn Diện (`dotnet test`)
```powershell
dotnet test .\BetaGiaiPhong.slnx --logger "console;verbosity=normal"
```
```text
Test Run Successful.
Total tests: 104
     Passed: 104
 Total time: 52.9560 Seconds
```
- **Tổng số bài kiểm tra:** 104/104 Pass (bao gồm 93 test methods độc lập và 104 test execution instances tính cả test theories).
  - **Unit Tests (7 tests):** `MockPaymentSecurityTests` (4 tests), `QrTokenProtectorTests` (6 tests: determinism, valid roundtrip, corruption detection, wrong key rejection, restart unseal resilience, PNG decoding roundtrip), `NavigationPropertyMappingTests` (1 test: kiểm tra toàn vẹn 22 FK navigation properties).
  - **Domain Integration Tests (21 tests):** `ReportingAndIntegrityTests` (2), `QrScanningTests` (6: valid scan, duplicate scan, concurrent scan race, forged token, wrong showtime, outside window), `PaymentWorkflowTests` (6: happy path, failure, idempotent duplicates, late payment review, invalid HMAC, amount tampering), `BookingConcurrencyTests` (7: seat race conditions).
  - **Web Integration Tests (76 instances / 65 methods):** `AuthenticationAndAuthorizationWebTests` (7), `PublicCatalogWebTests` (6), `WebSecurityTests` (5), `BookingAndOrderWebTests` (31 instances), `MockPaymentAndTicketWebTests` (27 instances).

### 5.2. Biên Dịch Cấu Hình Release (Release Build)
```powershell
dotnet build .\BetaGiaiPhong.slnx -c Release
```
```text
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:22.16
```

### 5.3. Kiểm Tra Biến Đổi Mô Hình Cơ Sở Dữ Liệu (EF Core Model Drift)
```powershell
dotnet ef migrations has-pending-model-changes --project .\src\BetaGiaiPhong.Data --startup-project .\src\BetaGiaiPhong.Data
```
```text
Build started...
Build succeeded.
No changes have been made to the model since the last migration.
```

### 5.4. Kiểm Tra Lỗ Hổng Bảo Mật Thư Viện (NuGet Vulnerability Audit)
```powershell
dotnet list package --vulnerable --include-transitive
```
```text
The given project `BetaGiaiPhong.Data` has no vulnerable packages given the current sources.
The given project `BetaGiaiPhong.Runner` has no vulnerable packages given the current sources.
The given project `BetaGiaiPhong.Web` has no vulnerable packages given the current sources.
The given project `BetaGiaiPhong.Tests` has no vulnerable packages given the current sources.
```

### 5.5. Kiểm Tra Tĩnh Cấu Trúc Schema & SQL Constraints
```powershell
python .\scripts\static_schema_checks.py
```
```text
PASS: 17 tables, 116 columns, 22 FKs; migration/snapshot/code mapping structurally consistent.
PASS: 36 SQL batches, 17 Down drops; C# delimiter checks; csproj/slnx XML.
```

### 5.6. Kiểm Tra Bất Biến Dữ Liệu Đa Bảng (Database Multi-Table Invariants)
```powershell
dotnet run --project .\src\BetaGiaiPhong.Runner -- verify
```
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

## 6. Trạng Thái Nhánh Git & Khuyến Nghị Hợp Nhất (Merge Recommendation)

- **Nhánh hiện tại:** `feat/mock-payment-ticketing`
- **Tác vụ đã hoàn tất:**
  - Toàn bộ bí mật đã được loại bỏ khỏi tệp được Git theo dõi.
  - Các tệp nhạy cảm không nằm trong staged changes.
  - Mã nguồn đã biên dịch sạch sẽ ở chế độ Release với 0 cảnh báo.
  - 104/104 ca kiểm thử pass 100%.
  - 0 vấn đề tồn đọng có mức độ nghiêm trọng P1/P2.
- **Kết luận:** **READY TO MERGE** (Sẵn sàng hợp nhất vào nhánh `main` khi có lệnh từ người phụ trách).
