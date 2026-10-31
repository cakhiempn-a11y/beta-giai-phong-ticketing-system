# Báo Cáo Kỹ Thuật Giai Đoạn BE-02
## Đặt Vé, Giữ Ghế, Quản Lý Đơn Hàng & Vòng Đời Đơn Hàng

**Dự án:** Phân tích, thiết kế và xây dựng hệ thống quản lý bán vé trực tuyến tại rạp chiếu phim Beta Giải Phóng  
**Bối cảnh học thuật:** Đề án tốt nghiệp Hệ thống thông tin quản lý (MIS) — Dữ liệu mô phỏng, không liên kết hệ thống thực tế của Beta Cinemas  
**Phiên bản:** BE-02 Baseline  
**Ngày hoàn thành:** 31/10/2026  
**Trạng thái:** HOÀN THÀNH TOÀN DIỆN (66/66 Tests Passed, 0 Vulnerabilities, 0 Model Drift, Release Build 0 Error/0 Warning)

---

## 1. Tóm Tắt Thực Thi (Executive Summary)

Giai đoạn **BE-02** đã hoàn thành việc triển khai toàn bộ luồng nghiệp vụ cốt lõi về **đặt vé, giữ ghế và quản lý đơn hàng** cho hệ thống bán vé rạp chiếu phim Beta Giải Phóng. Toàn bộ logic giao dịch được tích hợp liền mạch với tầng dữ liệu hiện có (`BetaGiaiPhong.Data`) và cơ sở dữ liệu Microsoft SQL Server, đảm bảo nguyên tắc ACID và chống bán vượt ghế (overselling) thông qua khóa mức dòng kết hợp mức cô lập giao dịch `Serializable`.

### Các kết quả nổi bật:
1. **Giao diện chọn ghế tương tác (Interactive Seat Selection UI):** Nâng cấp giao diện `/showtimes/{id}/seats` từ chế độ chỉ đọc sang chế độ tương tác, hỗ trợ chọn tối đa 8 ghế, tự động tính tổng tiền theo thời gian thực và hiển thị các trạng thái trực quan (`Trống`, `Đang chọn`, `Đang giữ`, `Đã bán`, `Khóa`).
2. **API & Endpoint đặt giữ ghế giao dịch (Transactional Seat Reservation):**
   - REST API: `POST /api/bookings/reserve` (trả về HTTP 201 Created cùng `ReservationResponseDto` và header `Location`).
   - MVC Action: `POST /bookings/reserve` (hỗ trợ submit form HTML, tự động chuyển hướng sang trang chi tiết đơn hàng).
   - Kiểm soát giữ chỗ 5 phút theo quy định mô phỏng của hệ thống, snapshot giá vé tức thời tại thời điểm đặt vào bảng `dbo.OrderSeats`.
3. **Quản lý danh sách và chi tiết đơn hàng của khách hàng (Order Query Services):**
   - MVC Views: `/orders` (danh sách đơn phân trang, nhãn trạng thái) và `/orders/{id}` (chi tiết hóa đơn, bảng ghế snapshot, đồng hồ đếm ngược thời gian thanh toán).
   - REST API: `GET /api/orders` (phân trang, tổng số đơn) và `GET /api/orders/{id}` (chi tiết đầy đủ).
   - Truy vấn tối ưu chỉ đọc qua `AsNoTracking()`, định dạng thời gian Việt Nam (`UTC+7`, `dd/MM/yyyy HH:mm:ss`), bảo mật kiểm tra quyền sở hữu tuyệt đối của tài khoản `CUSTOMER`.
4. **Hủy đơn hàng và xử lý đơn hết hạn (Order Cancellation & Expiration):**
   - `POST /api/orders/{id}/cancel` và `POST /orders/{id}/cancel`: Cho phép khách hàng chủ động hủy đơn đang chờ thanh toán (`PENDING_PAYMENT`), giải phóng ghế về trạng thái `AVAILABLE`.
   - Cơ chế tự động nhận diện và xử lý đơn hết hạn: Nếu khách hàng thao tác hủy hoặc thanh toán trên đơn đã quá thời hạn giữ ghế (`expires_utc <= now`), hệ thống chuyển trạng thái đơn sang `EXPIRED`, giải phóng ghế, ghi log kiểm toán `ORDER_EXPIRED` và từ chối với mã lỗi `ORDER_EXPIRED` (HTTP 409 Conflict).
5. **Hạ tầng bảo mật & Middleware:** Tinh chỉnh thứ tự Anti-CSRF token middleware, sinh cookie `XSRF-TOKEN` cho phiên đăng nhập, chuẩn hóa xử lý ngoại lệ nghiệp vụ RFC 7807 `ProblemDetails`.
6. **Mở rộng kiểm thử tự động:** Xây dựng 20 ca kiểm thử Web Integration Tests mới trong `BookingAndOrderWebTests.cs`, nâng tổng số test của toàn bộ solution lên **66 tests** (toàn bộ 66 tests đều PASS 100%).

---

## 2. Ranh Giới Phạm Vi BE-02 (Scope Boundaries)

Tuân thủ nghiêm ngặt chỉ đạo kiến trúc và mục tiêu giai đoạn:
- **Thuộc phạm vi BE-02:**
  - Chọn ghế và validate ràng buộc 1 đến 8 ghế.
  - Giao dịch giữ ghế với thời hạn 5 phút.
  - Truy vấn danh sách và chi tiết đơn hàng của khách hàng hiện tại.
  - Hủy đơn hàng và giải phóng ghế của chính khách hàng sở hữu đơn.
  - Nhận diện và chuyển trạng thái đơn hết hạn (`EXPIRED`).
  - Giao diện Razor Views và RESTful API endpoints tương ứng.
- **TUYỆT ĐỐI KHÔNG thuộc phạm vi BE-02 (Dành cho các giai đoạn sau):**
  - Không xử lý thanh toán hay webhook thanh toán giả lập (dành cho **BE-03**).
  - Không phát hành vé điện tử hay mã QR mã hóa (dành cho **BE-03**).
  - Không xây dựng background worker tự động chạy ngầm định kỳ (dành cho **BE-04**).
  - Không thay đổi schema cơ sở dữ liệu hay tạo thêm migration mới.

---

## 3. Kiến Trúc & Cấu Trúc Thành Phần BE-02

### 3.1. Tầng Dữ Liệu (`BetaGiaiPhong.Data`)
- **`OrderLifecycleService.cs`:**
  - Nâng cấp phương thức `FinishPendingAsync`: Bổ sung xử lý khi đơn hàng ở trạng thái `PENDING_PAYMENT` nhưng đã quá thời hạn `ExpiresUtc <= now`. Khi đó, đơn được chuyển sang `OrderStates.Expired`, giải phóng toàn bộ ghế đang giữ trong suất chiếu, ghi nhận sự kiện `ORDER_EXPIRED` vào bảng `dbo.AuditLogs`, commit giao dịch và phát sinh ngoại lệ `BusinessRuleException("ORDER_EXPIRED", "Order has expired and cannot be cancelled.")`.
  - Đảm bảo các đơn đã ở trạng thái `PAID`, `CANCELLED` hoặc `EXPIRED` không thể bị hủy lại, trả về mã lỗi nghiệp vụ chuẩn `INVALID_ORDER_STATE` hoặc `ORDER_EXPIRED`.

### 3.2. Tầng Ứng Dụng & Web (`BetaGiaiPhong.Web`)

```text
src/BetaGiaiPhong.Web/
├── Controllers/
│   ├── BookingsController.cs          # POST /bookings/reserve (MVC Form submit, redirect /orders/{id})
│   └── OrdersController.cs            # GET /orders, GET /orders/{id}, POST /orders/{id}/cancel
├── Api/
│   ├── BookingsApiController.cs       # POST /api/bookings/reserve (REST API đặt vé, trả về 201 Created)
│   └── OrdersApiController.cs         # GET /api/orders, GET /api/orders/{id}, POST /api/orders/{id}/cancel
├── Models/
│   ├── Requests/
│   │   └── ReserveSeatsRequest.cs     # Request DTO (ShowtimeId, danh sách 1-8 SeatIds)
│   ├── Responses/
│   │   └── BookingDtos.cs             # ReservationResponseDto, OrderSummaryDto, OrderSeatItemDto, OrderDetailDto, CancelOrderResultDto
│   └── ViewModels/
│       ├── OrderListViewModel.cs      # ViewModel danh sách đơn phân trang kèm flash messages
│       └── OrderDetailViewModel.cs    # ViewModel chi tiết đơn hàng kèm thông tin thanh toán & đồng hồ đếm ngược
├── Services/
│   ├── IBookingQueryService.cs        # Interface truy vấn đơn hàng
│   └── BookingQueryService.cs         # Hiện thực truy vấn bất đồng bộ, AsNoTracking(), định dạng UTC+7
├── Views/
│   ├── Showtimes/
│   │   └── Seats.cshtml               # Nâng cấp giao diện chọn ghế tương tác, form submit POST
│   ├── Orders/
│   │   ├── Index.cshtml               # Danh sách đơn hàng, nhãn trạng thái và phân trang
│   │   └── Details.cshtml             # Chi tiết đơn hàng, snapshot ghế, bộ đếm ngược 5 phút, nút hủy đơn
│   └── Shared/
│       └── _Layout.cshtml             # Bổ sung link "Đơn hàng của tôi" cho khách hàng đã đăng nhập
└── wwwroot/css/
    └── site.css                       # Bổ sung style cho ghế tương tác: .seat-bookable, .seat-selected
```

---

## 4. Các Vấn Đề Kỹ Thuật Đã Phát Hiện & Khắc Phục

Trong quá trình hiện thực và kiểm thử tích hợp, các vấn đề kỹ thuật sau đã được phát hiện, cô lập nguyên nhân gốc rễ và xử lý triệt để:

| Vấn đề | Nguyên nhân gốc rễ | Giải pháp kỹ thuật | Trạng thái |
| :--- | :--- | :--- | :--- |
| **Lỗi biên dịch CS1061** trong `BookingQueryService.cs` | Sử dụng nhầm tên thuộc tính `Seat.RowCode` và `Seat.SeatNumber` (tên thực tế trong Entity là `RowLabel` và `ColumnNo`). | Điều chỉnh ánh xạ sang `s.RowLabel` và `s.ColumnNo`. | **Đã sửa** |
| **Lỗi 400 Bad Request trên POST API có Antiforgery** | Middleware cấp phát cookie `XSRF-TOKEN` được đặt trước `app.UseAuthentication()`, dẫn đến token được sinh ra cho định danh Anonymous thay vì định danh `CUSTOMER` sau đăng nhập. | Di chuyển middleware cấp phát `XSRF-TOKEN` ra sau `app.UseAuthorization()`, đảm bảo token được ký đúng định danh `ClaimsPrincipal` của khách hàng. | **Đã sửa** |
| **Lỗi 500 DeveloperExceptionPage bắt ngoại lệ nghiệp vụ** | `app.UseDeveloperExceptionPage()` trong môi trường Development được đăng ký bên trong `GlobalExceptionHandlerMiddleware`, chặn việc xử lý lỗi RFC 7807 của middleware. | Đăng ký `GlobalExceptionHandlerMiddleware` sau `UseDeveloperExceptionPage()`, giúp bắt trực tiếp `BusinessRuleException` từ các route `/api/*` và map chuẩn sang 400, 403, 404, 409, 422. | **Đã sửa** |
| **Vi phạm ràng buộc `CK_Orders_07`** trong test đơn quá hạn | Ràng buộc yêu cầu `expires_utc > created_utc`. Khi test cập nhật `ExpiresUtc = DateTime.UtcNow.AddMinutes(-10)` mà giữ nguyên `CreatedUtc` hiện tại thì `expires_utc < created_utc`. | Cập nhật đồng thời `CreatedUtc = DateTime.UtcNow.AddMinutes(-15)` và `ExpiresUtc = DateTime.UtcNow.AddMinutes(-10)`, bảo đảm tính hợp lệ của constraint. | **Đã sửa** |
| **Vi phạm ràng buộc `CK_Orders_09`** trong test hủy đơn đã thanh toán | Ràng buộc yêu cầu nếu `status = 'PAID'` thì `paid_utc IS NOT NULL`. Khi test gán trạng thái `PAID` nhưng để `paid_utc` là `null` thì bị cơ sở dữ liệu chặn. | Gán đồng thời `PaidUtc = DateTime.UtcNow` khi giả lập đơn đã thanh toán. | **Đã sửa** |
| **Lỗi kiểm tra toàn vẹn `TC_INT_01`** (PAID orders missing tickets) | Các ca test giả lập đơn `PAID` trước đó chưa có cơ chế hoàn nguyên trạng thái khi kết thúc, để lại dữ liệu rác trong database dùng chung `BetaGiaiPhong_Integration_TestDb`. | Sử dụng khối `try ... finally` trong `TC_WEB_BOOK_13` để hoàn nguyên trạng thái đơn về `CANCELLED` (`paid_utc = null`), dọn dẹp các bản ghi mồ côi trên database. | **Đã sửa** |

---

## 5. Danh Mục 20 Ca Kiểm Thử Tích Hợp BE-02 (`BookingAndOrderWebTests`)

Tất cả 20 ca kiểm thử tích hợp Web đều được thiết kế chặt chẽ, chạy trên cơ sở dữ liệu SQL Server thực tế qua `CustomWebApplicationFactory`:

| Mã Test | Tên Ca Kiểm Thử | Mục Tiêu & Nghiệp Vụ Kiểm Chứng | Kết Quả |
| :--- | :--- | :--- | :--- |
| **TC_WEB_BOOK_01** | `Guest_PostReserve_Returns401Unauthorized` | Khách vãng lai chưa đăng nhập gọi API đặt vé bị từ chối với HTTP 401. | **PASS** |
| **TC_WEB_BOOK_02** | `NonCustomerRole_PostReserve_Returns403Forbidden` | Tài khoản nhân viên/quản lý (`STAFF`) cố tình gọi API đặt vé bị chặn với HTTP 403. | **PASS** |
| **TC_WEB_BOOK_03** | `Customer_ReserveSingleSeat_Returns201Created` | Khách hàng đặt giữ 1 ghế thành công: trả về HTTP 201, OrderCode dạng `BGP-*`, tổng tiền chính xác, trạng thái `PENDING_PAYMENT`. | **PASS** |
| **TC_WEB_BOOK_04** | `Customer_ReserveMultipleSeats_Returns201Created` | Khách hàng đặt giữ nhiều ghế (3 ghế) thành công với tổng tiền và danh sách ghế chính xác. | **PASS** |
| **TC_WEB_BOOK_05** | `ReserveMoreThan8Seats_Returns400BadRequest` | Đặt quá giới hạn quy định (> 8 ghế) bị từ chối ngay với HTTP 400 Bad Request. | **PASS** |
| **TC_WEB_BOOK_06** | `ReserveDuplicateSeats_Returns400BadRequest` | Gửi danh sách ghế có mã trùng lặp bị từ chối với HTTP 400 Bad Request. | **PASS** |
| **TC_WEB_BOOK_07** | `ReserveEmptySeatList_Returns400BadRequest` | Gửi danh sách ghế rỗng bị từ chối với HTTP 400 Bad Request. | **PASS** |
| **TC_WEB_BOOK_08** | `ReserveAlreadyHeldSeat_Returns409Conflict` | Khách hàng thứ hai cố gắng đặt ghế đang được khách hàng thứ nhất giữ bị từ chối với HTTP 409 Conflict. | **PASS** |
| **TC_WEB_BOOK_09** | `ReserveSeatFromAnotherRoom_ReturnsConflict` | Đặt ghế không tồn tại hoặc không thuộc phòng chiếu của suất chiếu bị từ chối với HTTP 409 Conflict. | **PASS** |
| **TC_WEB_BOOK_10** | `CustomerA_CannotViewCustomerB_Order_Returns404NotFound` | Khách hàng A cố gắng xem chi tiết đơn hàng của Khách hàng B bị từ chối với HTTP 404 (bảo mật, không lộ sự tồn tại của đơn). | **PASS** |
| **TC_WEB_BOOK_11** | `CustomerA_CannotCancelCustomerB_Order_Returns403Or404` | Khách hàng A cố gắng hủy đơn hàng của Khách hàng B bị từ chối với HTTP 403 Forbidden. | **PASS** |
| **TC_WEB_BOOK_12** | `CancelPendingOrder_Succeeds_MarksCancelledAndReleasesSeats` | Khách hàng hủy đơn hợp lệ: đơn chuyển sang `CANCELLED`, ghế trong suất chiếu lập tức chuyển về `AVAILABLE`. | **PASS** |
| **TC_WEB_BOOK_13** | `CancelPaidOrder_Returns422UnprocessableEntity` | Khách hàng không thể hủy đơn hàng đã thanh toán thành công (`PAID`), trả về HTTP 422 Unprocessable Entity. | **PASS** |
| **TC_WEB_BOOK_14** | `CancelExpiredOrder_TransitionsToEXPIRED_Returns409Conflict` | Hủy đơn đã quá thời hạn giữ ghế (5 phút): hệ thống chuyển đơn sang `EXPIRED`, giải phóng ghế và trả về HTTP 409 Conflict. | **PASS** |
| **TC_WEB_BOOK_15** | `GetOrder_IsReadOnly_DoesNotMutateDatabase` | Gọi truy vấn xem đơn nhiều lần liên tiếp không làm thay đổi bất kỳ trường dữ liệu hay `rowversion` nào trong database. | **PASS** |
| **TC_WEB_BOOK_16** | `PostReserve_WithoutAntiforgeryToken_Returns400BadRequest` | Gửi yêu cầu đặt vé mà không có Anti-CSRF token bị từ chối với HTTP 400. | **PASS** |
| **TC_WEB_BOOK_17** | `OrderTotalVnd_MatchesOrderSeatsSnapshot` | Tổng tiền đơn hàng `TotalVnd` khớp chính xác 100% với tổng snapshot giá các ghế trong `dbo.OrderSeats`. | **PASS** |
| **TC_WEB_BOOK_18** | `MvcSeatSelectionFlow_RedirectsToOrderDetails` | Luồng người dùng submit form chọn ghế trên giao diện Razor MVC chuyển hướng chuẩn xác về trang `/orders/{id}`. | **PASS** |
| **TC_WEB_BOOK_19** | `GetMyOrders_PaginationAndList_ReturnsAccurateOrders` | API danh sách đơn hàng của khách hàng phân trang chuẩn xác và trả về đầy đủ các trường dữ liệu. | **PASS** |
| **TC_WEB_BOOK_20** | `OrderLifecycleService_ExpireDueAsync_CleansUpExpiredOrders` | Phương thức quét đơn quá hạn của `OrderLifecycleService` nhận diện đúng và chuyển các đơn quá hạn sang `EXPIRED`. | **PASS** |

---

## 6. Bằng Chứng Thực Thi & Xác Minh Chất Lượng (Quality Validation Evidence)

### 6.1. Biên Dịch Cấu Hình Release (Release Build)
```text
dotnet build .\BetaGiaiPhong.slnx -c Release
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

### 6.2. Kiểm Tra Lệch Mô Hình EF Core (Model Drift Check)
```text
dotnet ef migrations has-pending-model-changes --project .\src\BetaGiaiPhong.Data --startup-project .\src\BetaGiaiPhong.Data
No changes have been made to the model since the last migration.
```

### 6.3. Quét Lỗ Hổng Bảo Mật Gói Phụ Thuộc (Vulnerability Audit)
```text
dotnet list package --vulnerable --include-transitive
The given project `BetaGiaiPhong.Data` has no vulnerable packages.
The given project `BetaGiaiPhong.Runner` has no vulnerable packages.
The given project `BetaGiaiPhong.Web` has no vulnerable packages.
The given project `BetaGiaiPhong.Tests` has no vulnerable packages.
```

### 6.4. Kiểm Tra Schema Tĩnh (Static Schema Check)
```text
python .\scripts\static_schema_checks.py
PASS: 17 tables, 116 columns, 22 FKs; migration/snapshot/code mapping structurally consistent.
PASS: 36 SQL batches, 17 Down drops; C# delimiter checks; csproj/slnx XML.
```

### 6.5. Chạy Runner End-to-End Scenarios
```text
# Smoke test
dotnet run --project .\src\BetaGiaiPhong.Runner -- smoke
SMOKE PASSED: order 244, payment 109, ticket 82
Verified: PAID, QR accepted once, repeated scan blocked, callback idempotent.

# Multi-table integrity verification
dotnet run --project .\src\BetaGiaiPhong.Runner -- verify
Wrong room/seat: 0 violation(s)
SOLD seat without PAID order: 0 violation(s)
PAID orders missing tickets: 0 violation(s)
Order price snapshot mismatch: 0 violation(s)
Late payment improperly SUCCESS: 0 violation(s)
INTEGRITY PASSED (static consistency checks; run race test separately).

# High-concurrency race test
dotnet run --project .\src\BetaGiaiPhong.Runner -- race
RACE PASSED: one SUCCESS and one CONFLICT for a single showtime seat.
```

### 6.6. Kết Quả Chạy Toàn Bộ Test Suite
```text
dotnet test .\BetaGiaiPhong.slnx --logger "console;verbosity=normal"
Test Run Successful.
Total tests: 66
     Passed: 66
     Failed: 0
 Total time: 38.2204 Seconds
```

---

## 7. Đánh Giá Sẵn Sàng Chuyển Sang Giai Đoạn BE-03

Giai đoạn **BE-02** đã hoàn tất với chất lượng kỹ thuật cao nhất:
- Mã nguồn sạch sẽ, không có cảnh báo biên dịch, không có model drift hay vi phạm ràng buộc dữ liệu.
- Phân định ranh giới nghiệp vụ rõ ràng giữa BE-02 (đặt chỗ, giữ ghế, hủy đơn) và BE-03 (thanh toán, xuất vé QR).
- Toàn bộ cơ chế giao dịch, bảo vệ tranh chấp ghế và quản lý vòng đời đơn hàng đã được kiểm thử tự động toàn diện.
- **Dự án sẵn sàng 100% để bước vào Giai đoạn BE-03: Triển khai Cổng thanh toán giả lập (Mock Payment Gateway), Webhook HMAC-SHA256, Phát hành vé điện tử mã hóa QR (AES-256-GCM) và API soát vé cho nhân viên rạp.**
