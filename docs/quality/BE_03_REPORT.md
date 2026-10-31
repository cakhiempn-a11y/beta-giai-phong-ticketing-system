# Báo Cáo Kỹ Thuật Giai Đoạn BE-03
## Thanh Toán Giả Lập, Webhook Xác Thực, Vé Điện Tử & Hiển Thị Mã QR
### Kiểm Tra Kỹ Thuật Toàn Diện, Bảo Mật Mật Mã Học & Trạng Thái Sẵn Sàng Hợp Nhất (Merge Readiness)

**Dự án:** Phân tích, thiết kế và xây dựng hệ thống quản lý bán vé trực tuyến tại rạp chiếu phim Beta Giải Phóng  
**Bối cảnh học thuật:** Đề án tốt nghiệp Hệ thống thông tin quản lý (MIS) — Dữ liệu mô phỏng, không liên kết hệ thống thực tế của Beta Cinemas  
**Phiên bản:** BE-03 Final Verification & Merge Readiness  
**Ngày hoàn thành:** 31/10/2026  
**Nhánh Git:** `feat/mock-payment-ticketing`  
**Tài khoản GitHub bắt buộc:** `cakhiempn-a11y` (`320607805+cakhiempn-a11y@users.noreply.github.com`)  
**Trạng thái:** HOÀN THÀNH TOÀN DIỆN (95/95 Tests Passed, 0 Vulnerabilities, 0 Model Drift, Release Build 0 Error/0 Warning, 6/6 Multi-Table Invariants Passed) — **READY TO MERGE**

---

## 1. Tóm Tắt Thực Thi (Executive Summary)

Giai đoạn **BE-03** đã hoàn thành việc xây dựng toàn bộ quy trình **thanh toán giả lập (Mock Payment), webhook xác thực bảo mật HMAC-SHA256, phát hành vé điện tử và hiển thị mã QR động** cho hệ thống bán vé rạp chiếu phim Beta Giải Phóng.

Hệ thống tuân thủ chặt chẽ nguyên tắc tái sử dụng tối đa logic nghiệp vụ từ tầng dữ liệu (`BetaGiaiPhong.Data`) đã được kiểm chứng ở BE-01/BE-02, tuyệt đối không tạo thêm `PaymentService` thứ hai ở tầng Web, không phát sinh bất kỳ migration mới nào làm sai lệch database schema, và không sử dụng các dịch vụ tạo QR của bên thứ ba qua mạng Internet.

### Các kết quả nổi bật của giai đoạn BE-03:
1. **Mock Payment Contract & Checkout UI:**
   - Cung cấp API `POST /api/orders/{id}/payments/mock-attempt` bảo vệ bởi Cookie Auth (`CUSTOMER`), Anti-CSRF, trích xuất giá snapshot máy chủ từ database, ngăn chặn hoàn toàn việc can thiệp giá từ client.
   - Trang thanh toán MVC `GET /orders/{id}/checkout` với bộ đếm ngược 5 phút thời gian thực, bảng tóm tắt đơn hàng, và khuyến cáo học thuật minh bạch (không thu tiền thật, chỉ phục vụ mô phỏng).
2. **Authenticated Mock Webhook (`POST /api/webhooks/mock-payment`):**
   - Xác thực chữ ký `HMAC-SHA256` qua HTTP Header `X-Signature-SHA256` (hoặc `X-Signature`) sử dụng so sánh hằng số thời gian (`CryptographicOperations.FixedTimeEquals`).
   - Giới hạn kích thước payload tối đa 32 KB (`[RequestSizeLimit(32 * 1024)]`) chống DoS/OOM.
   - Xử lý đơn hàng qua `PaymentService.ApplyVerifiedMockAsync` với tính chất Idempotent tuyệt đối: ghi nhận `dbo.PaymentEvents`, phát hiện trùng lặp giao dịch và phản hồi HTTP 200 OK an toàn mà không phát hành vé thừa.
3. **Bộ Giả Lập Thanh Toán Phát Triển (Development-Only Simulator):**
   - Endpoint `POST /orders/{id}/demo-pay` chỉ kích hoạt trong môi trường `Development` và có cờ cấu hình `BGP_ENABLE_MOCK_PAYMENT_SIMULATOR=true`.
   - Tự động ký HMAC nội bộ và đưa qua đầy đủ luồng dịch vụ miền (`ApplyVerifiedMockAsync`), không sửa trực tiếp database.
4. **Máy Trạng Thái Thanh Toán & Vòng Đời Đơn Hàng:**
   - `PENDING_PAYMENT` -> `PAID`: Ghế chuyển `SOLD`, vé điện tử được phát hành ở trạng thái `ISSUED`.
   - `PENDING_PAYMENT` -> `FAILED`: Ghế được giải phóng về `AVAILABLE`, đơn hàng chuyển `FAILED`.
   - Thanh toán trễ (Late Payment - CR-01): Khi callback đến sau khi đơn đã hết hạn (`EXPIRED` hoặc `CANCELLED`), hệ thống ghi nhận thanh toán `NEEDS_REVIEW` để đối soát thủ công, chuyển đơn sang `EXPIRED`, cấp 0 vé và tuyệt đối không hồi sinh ghế đã hết hạn.
5. **Vé Điện Tử & Mã QR Bảo Mật (Zero Token Leakage):**
   - Sinh ảnh mã QR dạng `image/png` trực tiếp trên server sử dụng thư viện C# thuần túy `QRCoder` (`PngByteQRCode`), giải mã token từ `QrTokenProtector.Unseal(ticket.QrTokenCipher)`.
   - Header bảo mật `Cache-Control: no-store` ngăn ngừa caching vé trên proxy/trình duyệt.
   - API JSON `GET /api/orders/{id}/tickets` chỉ trả về metadata vé và URL ảnh QR, tuyệt đối không làm rò rỉ token gốc, cipher hay digest hash.
6. **Bộ Kiểm Thử Tự Động & Bất Biến Toàn Vẹn:**
   - Bổ sung 22 ca kiểm thử tích hợp chuyên sâu trong `MockPaymentAndTicketWebTests.cs` (`TC_WEB_PAY_01` đến `TC_WEB_PAY_22`).
   - Toàn bộ solution đạt **95/95 Tests Passed** (100% pass trên SQL Server thật).
   - Kiểm tra bất biến đa bảng đạt 6/6 Pass (0 violations).

---

## 2. Ranh Giới Phạm Vi BE-03 (Scope Boundaries)

Tuân thủ nghiêm ngặt chỉ đạo kiến trúc và phân định phạm vi:
- **Thuộc phạm vi BE-03:**
  - Hợp đồng thanh toán giả lập (`PaymentAttemptDto`, `MockPaymentWebhookRequest`, `DemoPaymentRequest`).
  - Giao diện Checkout MVC có đếm ngược thời gian giữ ghế và tóm tắt chi phí.
  - Webhook listener xác thực chữ ký HMAC-SHA256, xử lý idempotent và kích thước giới hạn 32 KB.
  - Trình giả lập thanh toán demo cho môi trường Development.
  - Chuyển đổi trạng thái đơn hàng (`PAID`, `FAILED`, `NEEDS_REVIEW`) và ghế (`SOLD`, `AVAILABLE`).
  - Giao diện xem vé điện tử `/orders/{id}/tickets` và API `/api/orders/{id}/tickets`.
  - Endpoint render ảnh QR Code dạng PNG `/tickets/{ticketId}/qr` với header `Cache-Control: no-store`.
  - Bảo vệ quyền sở hữu vé, đơn hàng và phòng chống IDOR.
- **TUYỆT ĐỐI KHÔNG thuộc phạm vi BE-03 (Dành cho BE-04 hoặc tương lai):**
  - Không tích hợp cổng thanh toán bên thứ ba thực tế (VNPAY, MoMo, ZaloPay, Stripe).
  - Không xây dựng giao diện quét mã QR cho nhân viên soát vé tại cửa rạp (thuộc **BE-04**).
  - Không xây dựng dashboard báo cáo doanh thu quản trị và màn hình quản lý phim/suất chiếu (thuộc **BE-04**).
  - Không thay đổi schema cơ sở dữ liệu hay tạo migration mới.

---

## 3. Kiến Trúc & Cấu Trúc Thành Phần Triển Khai

### 3.1. Sơ Đồ Khối Tương Tác Thanh Toán & Phát Hành Vé
```
[ Khách hàng / Browser ]
        │
        │ 1. GET /orders/{id}/checkout
        ▼
[ OrdersController.Checkout ] ────► Đọc snapshot đơn & đếm ngược giữ ghế
        │
        │ 2. POST /orders/{id}/demo-pay  (Development Simulator)
        ▼
[ OrdersController.DemoPay ] ─────► Ký HMAC-SHA256 nội bộ
        │
        │ 3. Gọi dịch vụ miền
        ▼
[ PaymentService.ApplyVerifiedMockAsync ]
        │
        ├──► 4. Ghi nhận dbo.PaymentEvents (Idempotent)
        ├──► 5. Chuyển đơn PAID, ghế SOLD
        └──► 6. Phát hành dbo.Tickets (AES-256-GCM cipher & SHA-256 hash)
        │
        │ 7. Redirect sang /orders/{id}/tickets
        ▼
[ OrdersController.Tickets ] ─────► Hiển thị vé điện tử
        │
        │ 8. <img src="/tickets/{ticketId}/qr" />
        ▼
[ TicketsController.GetQrCode ] ──► QrTokenProtector.Unseal() ──► QRCoder (PngByteQRCode)
```

### 3.2. Chi Tiết Các Lớp & Endpoint Mới Triển Khai

| Lớp / Endpoint | Loại | Mô tả chức năng & Ràng buộc bảo mật |
| :--- | :--- | :--- |
| `POST /api/orders/{id}/payments/mock-attempt` | REST API | Tạo phiên thanh toán giả lập. Yêu cầu Cookie Auth, Role `CUSTOMER`, Anti-CSRF, kiểm tra đơn `PENDING_PAYMENT` và chưa hết hạn. |
| `GET /orders/{id}/checkout` | MVC View | Trang thanh toán của khách. Hiển thị đếm ngược 5 phút, danh sách ghế, tổng tiền, form thanh toán mô phỏng và khuyến cáo học thuật. |
| `POST /api/webhooks/mock-payment` | REST Webhook | Nhận kết quả thanh toán từ gateway giả lập. Xác thực HMAC-SHA256 qua header `X-Signature-SHA256`, giới hạn 32 KB, idempotent. |
| `POST /orders/{id}/demo-pay` | MVC Action | Trình mô phỏng thanh toán nhanh cho môi trường phát triển (`Development` + `BGP_ENABLE_MOCK_PAYMENT_SIMULATOR=true`). Anti-CSRF, kiểm tra chủ sở hữu. |
| `GET /orders/{id}/tickets` | MVC View | Giao diện xem danh sách vé điện tử của đơn hàng `PAID`. Hiển thị ảnh QR, mã vé, phòng chiếu, ghế ngồi và giờ chiếu UTC+7. |
| `GET /api/orders/{id}/tickets` | REST API | Trả về danh sách vé dạng JSON cho đơn hàng `PAID`. Loại bỏ toàn bộ trường nhạy cảm (`qrTokenCipher`, `qrTokenHash`). |
| `GET /tickets/{ticketId}/qr` | Streaming Action | Render ảnh PNG mã QR cho vé. Header `Cache-Control: no-store`, kiểm tra quyền sở hữu vé (hoặc role nội bộ), giải mã token qua `QrTokenProtector`. |
| `TicketQueryService` | Application Service | Truy vấn vé điện tử định dạng giờ Việt Nam (UTC+7) và sinh mảng byte PNG qua `QRCoder.PngByteQRCode`. |
| `BookingQueryService` | Application Service | Bổ sung phương thức `GetCustomerOrderCheckoutAsync` phục vụ view checkout. |

---

## 4. Bảo Mật & Mật Mã Học (Security & Cryptography)

### 4.1. Xác Thực Chữ Ký Webhook HMAC-SHA256
- Chữ ký số được tính toán trên thông điệp thanh toán giả lập chuẩn:
  `canonical = $"event={msg.EventRefKey}|attempt={msg.AttemptKey}|txn={msg.ProviderTransactionRef}|order={msg.OrderCode}|amount={msg.AmountVnd}|status={msg.Status}";`
- Chữ ký được gửi kèm trong header `X-Signature-SHA256` hoặc `X-Signature`.
- Quá trình xác thực sử dụng hàm so sánh hằng số thời gian `CryptographicOperations.FixedTimeEquals` chống tấn công Timing Attack.
- Nếu chữ ký không hợp lệ hoặc dữ liệu bị can thiệp (ví dụ sửa `amount`), webhook từ chối ngay với HTTP 401 Unauthorized và ghi log cảnh báo bảo mật.

### 4.2. Bảo Vệ Mã QR Vé Điện Tử (AES-256-GCM & SHA-256)
- **Token Gốc (Plaintext Token):** Có định dạng ngẫu nhiên bảo mật cao `BGP-TKT-{GuidN}`. Token gốc **tuyệt đối không bao giờ được lưu trữ trực tiếp dưới dạng văn bản rõ** trong cơ sở dữ liệu.
- **Lưu Trữ Trong Cơ Sở Dữ Liệu:**
  - Trường `qr_hash` (`CHAR(64)`): Lưu giá trị băm SHA-256 của token để phục vụ tìm kiếm nhanh có chỉ mục (index lookup) khi soát vé tại quầy.
  - Trường `qr_token_cipher` (`NVARCHAR(MAX)`): Lưu chuỗi mã hóa xác thực AES-256-GCM kèm Nonce và Tag (`Base64(Nonce + Tag + Ciphertext)`).
- **Phục Hồi Khi Hiển Thị:** Chỉ khi khách hàng hợp lệ gửi yêu cầu tải ảnh QR tại `/tickets/{ticketId}/qr`, hệ thống mới dùng khóa bí mật `BGP_QR_AES_KEY_B64` để giải mã `QrTokenProtector.Unseal()`, nạp vào `QRCoder` sinh luồng byte PNG động và trả về client.
- **Chống Lưu Đệm (Anti-Caching):** Response của endpoint tải QR luôn chứa chỉ thị `Cache-Control: no-store, no-cache, must-revalidate` và `Pragma: no-cache`.

### 4.3. Quản Lý Khóa Phát Triển Ổn Định (Stable Development Keys)
Tại `appsettings.Development.json`, hệ thống cấu hình cặp khóa cố định cho môi trường phát triển cục bộ và kiểm thử tự động:
- `BGP_MOCK_HMAC_KEY_B64`: Khóa HMAC-SHA256 256-bit chuẩn Base64.
- `BGP_QR_AES_KEY_B64`: Khóa AES-256-GCM 256-bit chuẩn Base64.
- Tại `Program.cs`, hệ thống tự động đồng bộ cấu hình từ `IConfiguration` sang biến môi trường nếu biến môi trường chưa được thiết lập, bảo đảm ứng dụng khởi động mượt mà mà không làm gián đoạn trải nghiệm của lập trình viên.

---

## 5. Máy Trạng Thái Thanh Toán & Vòng Đời Đơn Hàng

| Trạng thái hiện tại | Sự kiện thanh toán | Trạng thái mới của Đơn | Trạng thái Ghế | Hành động phát hành vé |
| :--- | :--- | :--- | :--- | :--- |
| `PENDING_PAYMENT` | Webhook `SUCCESS` hợp lệ | `PAID` | `SOLD` | Phát hành vé điện tử `ISSUED`, lưu mã hóa AES-GCM và SHA-256 hash. |
| `PENDING_PAYMENT` | Webhook `FAILED` | `FAILED` | `AVAILABLE` | Giải phóng toàn bộ ghế được giữ về trạng thái trống. 0 vé được tạo. |
| `PAID` | Webhook trùng lặp (`Idempotent`) | `PAID` (giữ nguyên) | `SOLD` | Ghi log `PaymentEvent`, không phát hành thêm vé mới. Trả về HTTP 200. |
| `EXPIRED` | Webhook đến muộn `SUCCESS` | `EXPIRED` | Không đổi | Ghi nhận `dbo.Payments` với trạng thái `NEEDS_REVIEW`. Không hồi sinh ghế, không cấp vé. |
| `CANCELLED` | Webhook đến muộn `SUCCESS` | `CANCELLED` | Không đổi | Ghi nhận `dbo.Payments` với trạng thái `NEEDS_REVIEW`. Không hồi sinh ghế, không cấp vé. |
| `PENDING_PAYMENT` (đã quá giờ giữ) | Khách mở checkout hoặc gọi payment attempt | `EXPIRED` | `AVAILABLE` | Chuyển đơn sang `EXPIRED`, giải phóng ghế, từ chối giao dịch với mã 409 Conflict. |

---

## 6. Danh Mục 22 Ca Kiểm Thử Tích Hợp Mới (`MockPaymentAndTicketWebTests`)

Toàn bộ 22 ca kiểm thử mới được thực thi trên môi trường cơ sở dữ liệu SQL Server thực tế qua `CustomWebApplicationFactory`:

| Mã Test | Tên Ca Kiểm Thử | Nghiệp Vụ & Ràng Buộc Kiểm Chứng | Kết Quả |
| :--- | :--- | :--- | :--- |
| **TC_WEB_PAY_01** | `CreateMockAttempt_ValidPendingOrder_ReturnsAttemptDetails` | Tạo payment attempt thành công cho đơn PENDING_PAYMENT, trả về 200 OK kèm thông tin thanh toán chuẩn. | **PASS** |
| **TC_WEB_PAY_02** | `CreateMockAttempt_Unauthenticated_Returns401` | Khách vãng lai chưa đăng nhập gọi API attempt bị từ chối với HTTP 401 Unauthorized. | **PASS** |
| **TC_WEB_PAY_03** | `CreateMockAttempt_OtherCustomerOrder_Returns404` | Khách hàng A gọi attempt trên đơn của khách hàng B trả về 404 Not Found (chống IDOR). | **PASS** |
| **TC_WEB_PAY_04** | `CreateMockAttempt_NonPendingOrder_Returns409Conflict` | Gọi attempt trên đơn không ở trạng thái PENDING_PAYMENT bị từ chối 409 Conflict. | **PASS** |
| **TC_WEB_PAY_05** | `CreateMockAttempt_ExpiredReservation_MarksExpired_Returns409` | Gọi attempt khi đơn đã hết hạn giữ ghế: hệ thống tự chuyển đơn sang EXPIRED, nhả ghế và trả về 409. | **PASS** |
| **TC_WEB_PAY_06** | `Webhook_MissingSignatureHeader_Returns401Unauthorized` | Gọi webhook không có header chữ ký `X-Signature-SHA256` bị từ chối ngay với HTTP 401. | **PASS** |
| **TC_WEB_PAY_07** | `Webhook_InvalidSignature_Returns401Unauthorized` | Gọi webhook với chữ ký sai bị từ chối ngay với HTTP 401 Unauthorized. | **PASS** |
| **TC_WEB_PAY_08** | `Webhook_TamperedAmount_Returns401Unauthorized` | Dữ liệu amount bị sửa đổi so với chữ ký bị từ chối 401 Unauthorized. | **PASS** |
| **TC_WEB_PAY_09** | `Webhook_ValidPayment_TransitionsToPAID_SeatsSold_TicketsIssued` | Webhook thanh toán thành công: đơn chuyển PAID, ghế chuyển SOLD, vé được tạo ISSUED. | **PASS** |
| **TC_WEB_PAY_10** | `Webhook_DuplicatePayload_Returns200Idempotent` | Webhook gửi lặp lại thông tin cùng giao dịch: xử lý idempotent, trả về 200 và không nhân đôi vé. | **PASS** |
| **TC_WEB_PAY_11** | `Webhook_LatePaymentOnExpiredOrder_RecordsNeedsReview_NoTickets` | Thanh toán đến trễ khi đơn đã hết hạn: ghi nhận NEEDS_REVIEW, không cấp vé, giữ nguyên đơn EXPIRED. | **PASS** |
| **TC_WEB_PAY_12** | `Webhook_PaymentFailure_ReleasesSeatsAndMarksFailed` | Webhook báo thanh toán thất bại: đơn chuyển FAILED, giải phóng ghế về AVAILABLE. | **PASS** |
| **TC_WEB_PAY_13** | `Webhook_ConcurrentCallbacks_MaintainsDataIntegrity` | Gửi 5 callback webhook đồng thời: đúng 1 yêu cầu hoàn tất đơn, 4 yêu cầu còn lại nhận diện trùng lặp an toàn. | **PASS** |
| **TC_WEB_PAY_14** | `DemoPay_SimulatorDisabled_Returns404NotFound` | Gọi endpoint demo-pay khi cấu hình tắt simulator trả về HTTP 404 Not Found. | **PASS** |
| **TC_WEB_PAY_15** | `DemoPay_CustomerOrder_ExecutesPaymentFlowAndRedirects` | Khách gọi demo-pay hợp lệ: kích hoạt đầy đủ quy trình nghiệp vụ miền và chuyển hướng đến trang vé. | **PASS** |
| **TC_WEB_PAY_16** | `GetTickets_CustomerOrder_ReturnsTicketsWithVietnamTime` | Trang vé hiển thị đúng mã vé, phòng chiếu, vị trí ghế và định dạng giờ chiếu Việt Nam (UTC+7). | **PASS** |
| **TC_WEB_PAY_17** | `GetQrCode_ReturnsValidPng_DecryptsToValidToken` | Endpoint tải mã QR trả về ảnh định dạng `image/png`, header `no-store`, giải mã token chính xác. | **PASS** |
| **TC_WEB_PAY_18** | `TicketApi_DoesNotLeakRawTokenOrCipher` | API vé JSON trả về URL ảnh QR, tuyệt đối không làm lộ token thô, cipher hay hash bí mật. | **PASS** |
| **TC_WEB_PAY_19** | `GetQrCode_CrossCustomerAccess_Returns404NotFound` | Khách A truy cập mã QR vé của Khách B bị từ chối 404 Not Found (chống IDOR). | **PASS** |
| **TC_WEB_PAY_20** | `CheckoutPage_RendersCountdownTimerAndMockDisclaimer` | Giao diện Checkout hiển thị đồng hồ đếm ngược 5 phút và khuyến cáo học thuật rõ ràng. | **PASS** |
| **TC_WEB_PAY_21** | `CheckoutPage_ExpiredOrder_RedirectsToDetailsWithError` | Mở trang checkout khi đơn đã hết hạn giữ ghế: chuyển hướng về chi tiết đơn kèm thông báo lỗi. | **PASS** |
| **TC_WEB_PAY_22** | `DatabaseInvariants_ZeroViolationsAfterPayments` | Kiểm tra trực tiếp trên SQL Server sau các luồng thanh toán: khẳng định 0 vi phạm trên cả 6 bất biến dữ liệu. | **PASS** |

---

## 7. Bằng Chứng Xác Minh Kỹ Thuật (Verification Evidence)

### 7.1. Kết Quả Kiểm Thử Toàn Diện (`dotnet test`)
```text
Test Run Successful.
Total tests: 95
     Passed: 95
 Total time: 50.9255 Seconds
```
- Phân bổ 95 bài test:
  - **Data Layer / Unit Tests (7 tests):** `MockPaymentSecurityTests`, `QrTokenProtectorTests`, `NavigationPropertyMappingTests`.
  - **Data Layer / Integration Tests (21 tests):** `ReportingAndIntegrityTests`, `QrScanningTests`, `PaymentWorkflowTests`, `BookingConcurrencyTests`.
  - **Web Layer / Integration Tests (67 tests):** `AuthenticationAndAuthorizationWebTests` (7), `PublicCatalogWebTests` (6), `WebSecurityTests` (5), `BookingAndOrderWebTests` (27 instances), `MockPaymentAndTicketWebTests` (22).

### 7.2. Biên Dịch Cấu Hình Release
```powershell
dotnet build .\BetaGiaiPhong.slnx -c Release
# Build succeeded: 0 Warning(s), 0 Error(s)
```

### 7.3. Kiểm Tra Biến Đổi Schema (EF Core Model Drift)
```powershell
dotnet ef migrations has-pending-model-changes --project .\src\BetaGiaiPhong.Data --startup-project .\src\BetaGiaiPhong.Data
# No changes have been made to the model since the last migration.
```

### 7.4. Kiểm Tra Lỗ Hổng Thư Viện (Vulnerability Audit)
```powershell
dotnet list package --vulnerable --include-transitive
# The given project `BetaGiaiPhong.Data` has no vulnerable packages given the current sources.
# The given project `BetaGiaiPhong.Runner` has no vulnerable packages given the current sources.
# The given project `BetaGiaiPhong.Web` has no vulnerable packages given the current sources.
# The given project `BetaGiaiPhong.Tests` has no vulnerable packages given the current sources.
```

### 7.5. Kiểm Tra Tĩnh Schema & Ràng Buộc DDL
```powershell
python .\scripts\static_schema_checks.py
# PASS: 17 tables, 116 columns, 22 FKs; migration/snapshot/code mapping structurally consistent.
# PASS: 36 SQL batches, 17 Down drops; C# delimiter checks; csproj/slnx XML.
```

### 7.6. Kiểm Tra Bất Biến Toàn Vẹn Đa Bảng (Database Multi-Table Invariants)
```powershell
dotnet run --project .\src\BetaGiaiPhong.Runner -- verify
# Wrong room/seat: 0 violation(s)
# SOLD seat without PAID order: 0 violation(s)
# PAID orders missing tickets: 0 violation(s)
# Order price snapshot mismatch: 0 violation(s)
# Late payment improperly SUCCESS: 0 violation(s)
# Dead order still holds seat: 0 violation(s)
# INTEGRITY PASSED (static consistency checks; run race test separately).
```

---

## 8. Kết Luận & Khuyến Nghị Cho BE-04

Giai đoạn BE-03 đã được triển khai hoàn chỉnh, đạt chất lượng sản phẩm công nghiệp (production-ready) trên nền tảng kiến trúc .NET 10 và SQL Server.

Mã nguồn tại nhánh `feat/mock-payment-ticketing` đã sẵn sàng hợp nhất vào `main`.

**Đề xuất cho giai đoạn tiếp theo (BE-04):**
1. **Giao Diện Soát Vé Dành Cho Nhân Viên (Staff QR Scanner):** Tận dụng `TicketScanningService` để xây dựng màn hình quét/nhập mã QR cho tài khoản có vai trò `STAFF`, kiểm tra vé hợp lệ, chống quét lặp lại (CR-08) và kiểm tra khung giờ quét hợp lệ (CR-09).
2. **Dashboard Báo Cáo Doanh Thu Quản Trị (Manager / Admin Reporting):** Trực quan hóa dữ liệu từ view `dbo.vw_ShowtimeSales` hiển thị doanh thu theo phim, phòng chiếu và tỷ lệ lấp đầy ghế.
3. **Hosted Service Quét Đơn Hết Hạn Định Kỳ (Background Worker):** Xây dựng `IHostedService` định kỳ gọi `OrderLifecycleService.ExpireDueAsync` để tự động giải phóng ghế của các đơn hàng quá hạn 5 phút mà không phụ thuộc vào thao tác của người dùng.
