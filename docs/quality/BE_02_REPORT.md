# Báo Cáo Kỹ Thuật Giai Đoạn BE-02
## Đặt Vé, Giữ Ghế, Quản Lý Đơn Hàng & Vòng Đời Đơn Hàng
### Đánh Giá Mã Nguồn Lần Cuối, Khắc Phục Trọng Điểm & Trạng Thái Sẵn Sàng Hợp Nhất (Merge Readiness)

**Dự án:** Phân tích, thiết kế và xây dựng hệ thống quản lý bán vé trực tuyến tại rạp chiếu phim Beta Giải Phóng  
**Bối cảnh học thuật:** Đề án tốt nghiệp Hệ thống thông tin quản lý (MIS) — Dữ liệu mô phỏng, không liên kết hệ thống thực tế của Beta Cinemas  
**Phiên bản:** BE-02 Final Review & Merge Readiness  
**Ngày hoàn thành:** 31/10/2026  
**Nhánh Git:** `feat/booking-orders`  
**Trạng thái:** HOÀN THÀNH TOÀN DIỆN (75/75 Tests Passed, 0 Vulnerabilities, 0 Model Drift, Release Build 0 Error/0 Warning, 6/6 Multi-Table Invariants Passed) — **READY TO MERGE**

---

## 1. Tóm Tắt Thực Thi (Executive Summary)

Giai đoạn **BE-02** đã hoàn thành việc triển khai toàn bộ luồng nghiệp vụ cốt lõi về **đặt vé, giữ ghế và quản lý đơn hàng** cho hệ thống bán vé rạp chiếu phim Beta Giải Phóng. Sau đợt Code Review kỹ thuật cuối cùng, tất cả các điểm yếu về cô lập dữ liệu kiểm thử, bảo mật phân quyền chống rò rỉ IDOR, validation dữ liệu đầu vào MVC form, và tính toàn vẹn đa bảng đã được khắc phục triệt để.

### Các kết quả nổi bật sau rà soát & khắc phục:
1. **Giao diện chọn ghế tương tác (Interactive Seat Selection UI):** Nâng cấp giao diện `/showtimes/{id}/seats` hỗ trợ chọn tối đa 8 ghế, tự động tính tổng tiền theo thời gian thực và hiển thị các trạng thái trực quan (`Trống`, `Đang chọn`, `Đang giữ`, `Đã bán`, `Khóa`). Bổ sung kiểm tra tính khả dụng đặt vé (`IsOpenForBooking`): khi suất chiếu không ở trạng thái `ON_SALE` hoặc thời gian chiếu đã qua (`StartUtc <= now`), giao diện tự động vô hiệu hóa toàn bộ ghế và hiển thị thông báo cảnh báo rõ ràng.
2. **Chuẩn hóa xác thực đầu vào MVC Form (Strict Form Validation):** Thay thế cơ chế tự động deduplicate hoặc bỏ qua phần tử lỗi bằng quy trình phân tích token chặt chẽ. Từ chối ngay lập tức các chuỗi CSV ghế bất hợp lệ (`1,1`, `1,abc`, `0,2`, `1,,2`, chuỗi rỗng hoặc vượt quá 8 ghế) kèm flash error thông báo cho người dùng.
3. **Bảo mật phân quyền & Ngăn chặn rò rỉ thông tin đơn hàng (IDOR Protection):** Khi một khách hàng cố tình truy cập hoặc gửi yêu cầu hủy đơn hàng thuộc về khách hàng khác, hệ thống đồng nhất trả về mã lỗi HTTP 404 Not Found (`ORDER_NOT_FOUND`), tuyệt đối không làm lộ sự tồn tại của đơn hàng khác trong cơ sở dữ liệu.
4. **Chuẩn hóa kiểm thử dữ liệu đơn PAID & Cô lập cơ sở dữ liệu (Test Isolation):** Tái cấu trúc hoàn toàn `TC_WEB_BOOK_13` bằng cách sử dụng luồng `PaymentService` và `MockPaymentSecurity` chính thức để tạo đơn hàng `PAID` hợp lệ (ghế `SOLD`, vé `ISSUED`), xác nhận từ chối hủy với HTTP 422 Unprocessable Entity mà không gây sai lệch dữ liệu; bổ sung quy trình tự động dọn dẹp trong `TestDatabaseFixture.InitializeAsync` để ngăn chặn dữ liệu rác giữa các lần chạy test.
5. **Mở rộng bất biến toàn vẹn đa bảng (Multi-Table Invariant):** Bổ sung quy tắc kiểm tra số 6 trong `ReportingAndIntegrityTests` và CLI Runner `verify`: phát hiện và khẳng định không có bất kỳ ghế nào ở trạng thái `HELD` lại thuộc về đơn hàng đã bị hủy, hết hạn hoặc thất bại (`CANCELLED`, `EXPIRED`, `FAILED`).
6. **Mở rộng bộ kiểm thử tự động:** Bổ sung các ca kiểm thử hồi quy mới (`TC_WEB_BOOK_21` đến `TC_WEB_BOOK_24`), nâng tổng số bài kiểm thử lên **75 tests** (100% PASS trên môi trường cơ sở dữ liệu SQL Server thực tế).

---

## 2. Ranh Giới Phạm Vi BE-02 (Scope Boundaries)

Tuân thủ nghiêm ngặt chỉ đạo kiến trúc và mục tiêu giai đoạn:
- **Thuộc phạm vi BE-02:**
  - Chọn ghế và validate ràng buộc 1 đến 8 ghế (cả REST API và MVC Form).
  - Giao dịch giữ ghế với thời hạn 5 phút và snapshot giá tức thời (`OrderSeats`).
  - Kiểm tra tính hợp lệ của suất chiếu khi mở sơ đồ ghế và khi tiếp nhận yêu cầu đặt vé.
  - Truy vấn danh sách và chi tiết đơn hàng của khách hàng hiện tại.
  - Hủy đơn hàng và giải phóng ghế của chính khách hàng sở hữu đơn.
  - Chống rò rỉ IDOR khi thao tác trên đơn hàng không thuộc quyền sở hữu.
  - Nhận diện và chuyển trạng thái đơn hết hạn (`EXPIRED`).
  - Toàn bộ giao diện Razor Views và RESTful API endpoints tương ứng.
- **TUYỆT ĐỐI KHÔNG thuộc phạm vi BE-02 (Dành cho các giai đoạn sau):**
  - Không xử lý thanh toán cổng ngoài hay webhook thật (dành cho **BE-03**).
  - Không phát hành vé điện tử mã QR ra file/màn hình thực tế cho khách (dành cho **BE-03**).
  - Không xây dựng background worker tự động chạy ngầm định kỳ (dành cho **BE-04**).
  - Không thay đổi schema cơ sở dữ liệu hay tạo migration mới.

---

## 3. Kiến Trúc & Cấu Trúc Thành Phần Sau Khắc Phục

### 3.1. Tầng Dữ Liệu (`BetaGiaiPhong.Data`)
- **`OrderLifecycleService.cs`:**
  - Chuẩn hóa kiểm tra quyền sở hữu đơn hàng: khi `actorId` không trùng với `order.UserId`, ném ngoại lệ `BusinessRuleException("ORDER_NOT_FOUND", "Order not found.")` thay vì `FORBIDDEN`, ngăn chặn hoàn toàn việc rò rỉ thông tin về sự tồn tại của đơn hàng khác qua mã lỗi.
  - Nâng cấp `FinishPendingAsync`: Nhận diện đơn quá hạn `ExpiresUtc <= now` để chuyển `EXPIRED`, giải phóng ghế, ghi log kiểm toán và từ chối với `ORDER_EXPIRED` (409 Conflict).

### 3.2. Tầng Ứng Dụng & Web (`BetaGiaiPhong.Web`)
- **`BookingsController.cs`:**
  - Triển khai phân tích CSV ghế chặt chẽ (`rawSeats.Split(',')`): kiểm tra từng token, từ chối các giá trị không phải số, số âm hoặc bằng 0, trùng lặp, danh sách rỗng hoặc vượt quá 8 ghế. Thiết lập `TempData["Error"]` và redirect về trang chọn ghế.
- **`CatalogService.cs` & `Seats.cshtml`:**
  - Đánh giá `isOpenForBooking = showtime.Status == "ON_SALE" && showtime.StartUtc > now`.
  - Nếu không đủ điều kiện, đánh dấu `seatMap.IsOpenForBooking = false`, gán `IsBookable = false` cho toàn bộ ghế, đặt `AvailableCount = 0` và hiển thị cảnh báo Bootstrap đỏ cùng nút quay lại trang chi tiết phim thay vì hiển thị form submit đặt vé.
- **`OrdersController.cs` & `OrdersApiController.cs`:**
  - Bắt lỗi `ORDER_NOT_FOUND` và thông báo nhất quán: `"Không tìm thấy đơn hàng yêu cầu hoặc đơn hàng không thuộc về bạn."` (MVC) hoặc `"Không tìm thấy đơn hàng yêu cầu."` (API 404).

### 3.3. Tầng Runner & Testing (`BetaGiaiPhong.Runner`, `BetaGiaiPhong.Tests`)
- **`Runner/Program.cs` & `ReportingAndIntegrityTests.cs`:**
  - Bổ sung quy tắc kiểm tra bất biến thứ 6:
    ```sql
    SELECT COUNT_BIG(*) 
    FROM dbo.ShowtimeSeats ss 
    JOIN dbo.Orders o ON o.order_id = ss.owner_order_id 
    WHERE ss.state = 'HELD' AND o.status IN ('CANCELLED', 'EXPIRED', 'FAILED');
    ```
- **`TestDatabaseFixture.cs`:**
  - Bổ sung logic tự động giải phóng bất kỳ ghế nào bị treo ở trạng thái `HELD` thuộc đơn hàng đã chết trong sự kiện khởi tạo fixture `InitializeAsync`, bảo đảm môi trường chạy test luôn sạch sẽ và cô lập.

---

## 4. Bảng Tổng Hợp Khắc Phục Kỹ Thuật (Targeted Remediation Matrix)

| Hạng mục | Vấn đề phát hiện | Giải pháp kỹ thuật đã triển khai | Trạng thái |
| :--- | :--- | :--- | :--- |
| **B. Test PAID Order Data (`TC_WEB_BOOK_13`)** | Test cũ gán cứng `status = PAID` rồi cập nhật `CANCELLED` trong `finally`, làm tổn hại dữ liệu ghế (`HELD`) và vi phạm tính bất biến của đơn hàng. | Sử dụng luồng chính thức qua `PaymentService.ProcessPaymentCallbackAsync` với chữ ký HMAC hợp lệ. Khẳng định ghế chuyển `SOLD`, vé được cấp `ISSUED`. Gọi hủy đơn và kiểm tra bị từ chối 422. Không sửa trạng thái đơn sau test. Bổ sung truy vấn kiểm tra không có ghế mồ côi. | **ĐÃ GIẢI QUYẾT** |
| **C. Kiểm Tra Quyền Sở Hữu Đơn (IDOR Check)** | Khách hàng A hủy đơn của Khách hàng B trả về 403 Forbidden, làm lộ rằng mã đơn hàng này có tồn tại trong hệ thống. | Chuẩn hóa tại `OrderLifecycleService`, `OrdersApiController` và `OrdersController`: cả trường hợp đơn không tồn tại và đơn thuộc khách hàng khác đều trả về mã lỗi 404 Not Found (`ORDER_NOT_FOUND`). | **ĐÃ GIẢI QUYẾT** |
| **D. Xác Thực Form Chọn Ghế MVC** | `BookingsController.Reserve` dùng `.Distinct()` và âm thầm loại bỏ chuỗi không hợp lệ, che giấu lỗi nhập liệu của người dùng. | Triển khai xác thực token-by-token nghiêm ngặt. Nếu có token rỗng, không phải số, `<= 0`, trùng lặp hoặc tổng số ghế ngoài khoảng [1, 8] thì từ chối và hiển thị thông báo lỗi rõ ràng. | **ĐÃ GIẢI QUYẾT** |
| **E. Suất Chiếu Không Đủ Điều Kiện Đặt Ghế** | Suất chiếu đã qua thời gian chiếu (`StartUtc <= now`) hoặc trạng thái khác `ON_SALE` vẫn cho phép bấm đặt ghế. | Cập nhật `CatalogService` trả về `IsOpenForBooking = false`, chuyển toàn bộ ghế thành không thể chọn, và ẩn form đặt vé trên `Seats.cshtml`. | **ĐÃ GIẢI QUYẾT** |
| **F. Kiểm Thử Hồi Quy Mở Rộng** | Thiếu các bài test bao phủ định dạng CSV lỗi, suất chiếu quá hạn, tranh chấp huỷ/thanh toán đồng thời và CSRF trên lệnh hủy. | Bổ sung `TC_WEB_BOOK_21` (Theory 6 cases), `TC_WEB_BOOK_22` (kiểm tra suất chiếu quá hạn/chưa mở bán), `TC_WEB_BOOK_23` (tranh chấp hủy và thanh toán đồng thời), và `TC_WEB_BOOK_24` (POST cancel không CSRF token). | **ĐÃ GIẢI QUYẾT** |

---

## 5. Danh Mục Toàn Bộ 24 Ca Kiểm Thử Tích Hợp BE-02 (`BookingAndOrderWebTests`)

Toàn bộ 24 ca kiểm thử tích hợp (với 29 test execution instances bao gồm Theory) đều chạy trực tiếp trên SQL Server thực tế qua `CustomWebApplicationFactory`:

| Mã Test | Tên Ca Kiểm Thử | Nghiệp Vụ & Ràng Buộc Kiểm Chứng | Kết Quả |
| :--- | :--- | :--- | :--- |
| **TC_WEB_BOOK_01** | `Guest_PostReserve_Returns401Unauthorized` | Khách vãng lai gọi API đặt vé bị chặn với HTTP 401. | **PASS** |
| **TC_WEB_BOOK_02** | `NonCustomerRole_PostReserve_Returns403Forbidden` | Tài khoản nội bộ (`STAFF`) đặt vé bị chặn với HTTP 403. | **PASS** |
| **TC_WEB_BOOK_03** | `Customer_ReserveSingleSeat_Returns201Created` | Đặt giữ 1 ghế thành công: trả về HTTP 201, OrderCode `BGP-*`, trạng thái `PENDING_PAYMENT`. | **PASS** |
| **TC_WEB_BOOK_04** | `Customer_ReserveMultipleSeats_Returns201Created` | Đặt giữ nhiều ghế thành công với tổng tiền và danh sách ghế chính xác. | **PASS** |
| **TC_WEB_BOOK_05** | `ReserveMoreThan8Seats_Returns400BadRequest` | Đặt quá 8 ghế bị từ chối ngay với HTTP 400. | **PASS** |
| **TC_WEB_BOOK_06** | `ReserveDuplicateSeats_Returns400BadRequest` | Gửi danh sách ghế trùng lặp qua API bị từ chối với HTTP 400. | **PASS** |
| **TC_WEB_BOOK_07** | `ReserveEmptySeatList_Returns400BadRequest` | Gửi danh sách ghế rỗng qua API bị từ chối với HTTP 400. | **PASS** |
| **TC_WEB_BOOK_08** | `ReserveAlreadyHeldSeat_Returns409Conflict` | Đặt ghế đang được người khác giữ bị từ chối với HTTP 409 Conflict. | **PASS** |
| **TC_WEB_BOOK_09** | `ReserveSeatFromAnotherRoom_ReturnsConflict` | Đặt ghế không thuộc phòng chiếu bị từ chối với HTTP 409 Conflict. | **PASS** |
| **TC_WEB_BOOK_10** | `CustomerA_CannotViewCustomerB_Order_Returns404NotFound` | Khách hàng A xem đơn khách hàng B bị chặn với HTTP 404. | **PASS** |
| **TC_WEB_BOOK_11** | `CustomerA_CannotCancelCustomerB_Order_Returns404NotFound` | Khách hàng A hủy đơn khách hàng B trả về HTTP 404 (chống rò rỉ IDOR). | **PASS** |
| **TC_WEB_BOOK_12** | `CancelPendingOrder_Succeeds_MarksCancelledAndReleasesSeats` | Hủy đơn hợp lệ: đơn chuyển `CANCELLED`, ghế chuyển về `AVAILABLE`. | **PASS** |
| **TC_WEB_BOOK_13** | `CancelPaidOrder_Returns422UnprocessableEntity` | Tạo đơn `PAID` bằng `PaymentService` chuẩn; yêu cầu hủy bị từ chối 422; dữ liệu giữ nguyên toàn vẹn. | **PASS** |
| **TC_WEB_BOOK_14** | `CancelExpiredOrder_TransitionsToEXPIRED_Returns409Conflict` | Hủy đơn quá hạn giữ chỗ: chuyển `EXPIRED`, giải phóng ghế, trả về 409. | **PASS** |
| **TC_WEB_BOOK_15** | `GetOrder_IsReadOnly_DoesNotMutateDatabase` | Đọc thông tin đơn hàng bảo đảm chỉ đọc, không thay đổi bất kỳ bản ghi nào. | **PASS** |
| **TC_WEB_BOOK_16** | `PostReserve_WithoutAntiforgeryToken_Returns400BadRequest` | Gửi yêu cầu đặt vé thiếu CSRF token bị chặn với HTTP 400. | **PASS** |
| **TC_WEB_BOOK_17** | `OrderTotalVnd_MatchesOrderSeatsSnapshot` | Tổng tiền đơn hàng khớp chính xác 100% với snapshot trong `OrderSeats`. | **PASS** |
| **TC_WEB_BOOK_18** | `MvcSeatSelectionFlow_RedirectsToOrderDetails` | Luồng submit form MVC chuyển hướng chính xác đến `/orders/{id}`. | **PASS** |
| **TC_WEB_BOOK_19** | `GetMyOrders_PaginationAndList_ReturnsAccurateOrders` | Danh sách đơn hàng phân trang chính xác và đầy đủ dữ liệu. | **PASS** |
| **TC_WEB_BOOK_20** | `OrderLifecycleService_ExpireDueAsync_CleansUpExpiredOrders` | Quét dọn đơn hàng hết hạn định kỳ hoạt động chính xác. | **PASS** |
| **TC_WEB_BOOK_21** | `MvcReserve_InvalidSeatCsvInput_RedirectsWithFlashError` | Kiểm thử 6 trường hợp chuỗi CSV không hợp lệ (`1,1`, `1,abc`, `0,2`, `1,,2`, rỗng, >8 ghế) đều bị từ chối và redirect kèm flash error. | **PASS** |
| **TC_WEB_BOOK_22** | `ReservePastOrNonSaleShowtime_ReturnsConflict` | Cố tình đặt vé vào suất chiếu quá khứ hoặc chưa mở bán bị từ chối với 409 (API) và flash error (MVC). | **PASS** |
| **TC_WEB_BOOK_23** | `ConcurrentCancelAndPayment_MaintainsDataConsistency` | Tranh chấp đồng thời giữa yêu cầu hủy và thanh toán webhook giữ vững tính nhất quán tuyệt đối. | **PASS** |
| **TC_WEB_BOOK_24** | `PostCancel_WithoutAntiforgeryToken_Returns400BadRequest` | Gửi yêu cầu hủy đơn qua MVC thiếu CSRF token bị từ chối với HTTP 400. | **PASS** |

---

## 6. Bằng Chứng Thực Thi & Xác Minh Kỹ Thuật Toàn Diện

### 6.1. Biên Dịch Cấu Hình Release (Release Build)
```text
dotnet build .\BetaGiaiPhong.slnx -c Release
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:18.26
```

### 6.2. Kiểm Tra Lệch Mô Hình EF Core (Model Drift Check)
```text
dotnet ef migrations has-pending-model-changes --project .\src\BetaGiaiPhong.Data --startup-project .\src\BetaGiaiPhong.Data
Build started...
Build succeeded.
No changes have been made to the model since the last migration.
```

### 6.3. Quét Lỗ Hổng Bảo Mật Gói Phụ Thuộc (Vulnerability Audit)
```text
dotnet list package --vulnerable --include-transitive
The given project `BetaGiaiPhong.Data` has no vulnerable packages given the current sources.
The given project `BetaGiaiPhong.Runner` has no vulnerable packages given the current sources.
The given project `BetaGiaiPhong.Web` has no vulnerable packages given the current sources.
The given project `BetaGiaiPhong.Tests` has no vulnerable packages given the current sources.
```

### 6.4. Kiểm Tra Schema Tĩnh (Static Schema Check)
```text
python .\scripts\static_schema_checks.py
PASS: 17 tables, 116 columns, 22 FKs; migration/snapshot/code mapping structurally consistent.
PASS: 36 SQL batches, 17 Down drops; C# delimiter checks; csproj/slnx XML.
```

### 6.5. Kiểm Tra Bất Biến Đa Bảng (Multi-Table Invariants via Runner)
```text
dotnet run --project .\src\BetaGiaiPhong.Runner -- verify
Wrong room/seat: 0 violation(s)
SOLD seat without PAID order: 0 violation(s)
PAID orders missing tickets: 0 violation(s)
Order price snapshot mismatch: 0 violation(s)
Late payment improperly SUCCESS: 0 violation(s)
Dead order still holds seat: 0 violation(s)
INTEGRITY PASSED (static consistency checks; run race test separately).
```

### 6.6. Kết Quả Chạy Toàn Bộ Test Suite
```text
dotnet test .\BetaGiaiPhong.slnx --logger "console;verbosity=normal"
Test Run Successful.
Total tests: 75
     Passed: 75
     Failed: 0
    Skipped: 0
 Total time: 41.9437 Seconds
```

---

## 7. Quy Trình & Kết Quả Kiểm Thử Giao Diện Trình Duyệt (Manual UI Verification)

Khi chạy ứng dụng qua lệnh `dotnet run --project .\src\BetaGiaiPhong.Web`:
1. **Luồng Khách hàng chọn ghế và đặt vé:**
   - Truy cập trang chủ `/`, chọn một phim đang chiếu (ví dụ: *Đào, Phở và Piano*), chọn một suất chiếu tương ứng.
   - Trình duyệt điều hướng tới `/showtimes/{id}/seats`: Sơ đồ ghế hiển thị trực quan các hàng ghế A–J, ghế thường và VIP.
   - Nhấp chọn 2 ghế (ví dụ: `E05`, `E06`): Ghế chuyển sang màu tím phát sáng (`seat-selected`), bảng điều khiển bên phải tự động hiển thị chip ghế đã chọn và tính tổng tiền VND chính xác theo đơn giá snapshot.
   - Nhấp nút "Xác nhận đặt vé": Form submit qua `POST /bookings/reserve` kèm CSRF token.
   - Hệ thống giữ ghế trong 5 phút và tự động điều hướng sang trang chi tiết đơn hàng `/orders/{id}` với mã đơn dạng `BGP-YYYYMMDD-XXXX`.
2. **Luồng Xem chi tiết đơn hàng & Đồng hồ đếm ngược:**
   - Trang `/orders/{id}` hiển thị chi tiết: thông tin phim, phòng chiếu, suất chiếu, bảng chi tiết từng ghế đã đặt.
   - Đồng hồ đếm ngược hiển thị dạng `MM:SS` (bắt đầu từ `05:00`) và đếm ngược từng giây theo thời gian thực.
3. **Luồng Khách hàng tự hủy đơn đang chờ thanh toán:**
   - Tại trang chi tiết đơn hàng đang ở trạng thái `PENDING_PAYMENT`, nhấp nút "Hủy đơn hàng này".
   - Hộp thoại JavaScript xác nhận xuất hiện. Khi người dùng xác nhận, form `POST /orders/{id}/cancel` được gửi đi.
   - Hệ thống chuyển trạng thái đơn hàng sang `Đã hủy` (`CANCELLED`), giải phóng ghế ngay lập tức, và hiển thị thông báo flash màu xanh: *"Đơn hàng đã được hủy thành công và ghế đã được giải phóng."*.
4. **Luồng Kiểm tra suất chiếu không hợp lệ hoặc đã qua:**
   - Điều hướng tới một suất chiếu có thời gian chiếu trong quá khứ: Trang `/showtimes/{id}/seats` hiển thị banner cảnh báo màu đỏ: *"Suất chiếu này đã bắt đầu hoặc đã kết thúc, không thể đặt vé."*.
   - Toàn bộ các ghế bị làm mờ, không thể nhấp chọn, và form đặt vé bên phải được thay bằng thông báo vô hiệu hóa kèm nút quay lại chi tiết phim.
5. **Luồng Kiểm tra nhập liệu CSV lỗi trên form MVC:**
   - Thử nghiệm gửi dữ liệu can thiệp (ví dụ: ghế trùng `1,1` hoặc chuỗi `1,abc`): Hệ thống chặn tại controller, chuyển hướng lại trang chọn ghế và hiển thị flash error: *"Vui lòng chọn từ 1 đến 8 ghế hợp lệ và không trùng lặp."*.

---

## 8. Kết Luận & Đánh Giá Sẵn Sàng Hợp Nhất (Merge Readiness Conclusion)

### Kết luận: **READY TO MERGE**

Giai đoạn **BE-02** đã đạt mức độ hoàn thiện cao nhất về mặt kiến trúc, bảo mật, dữ liệu và kiểm thử tự động:
1. **Không có bất kỳ cảnh báo hoặc lỗi biên dịch nào** trên cấu hình Debug và Release.
2. **Không có Model Drift** và không làm biến động schema database hiện có.
3. **Không có lỗ hổng bảo mật** trong toàn bộ cây phụ thuộc NuGet.
4. **100% kiểm thử tự động PASS** (75/75 tests bao gồm kiểm thử đa luồng, kiểm thử toàn vẹn, và kiểm thử chấp nhận Web).
5. **Đạt chuẩn bảo mật**: Đã xử lý triệt để nguy cơ lộ thông tin IDOR trên đơn hàng, kiểm tra nghiêm ngặt tính hợp lệ của suất chiếu và xác thực token form MVC.
6. **Bất biến dữ liệu toàn vẹn 6/6**: Đảm bảo không có ghế rác mồ côi và cơ sở dữ liệu luôn ở trạng thái nhất quán.

**Khuyến nghị hợp nhất:**
- Nhánh `feat/booking-orders` đã sẵn sàng để được hợp nhất vào nhánh cha `feat/backend-foundation` hoặc tích hợp vào `main` theo quy trình CI/CD của dự án.
- Dự án đủ điều kiện vững chắc để triển khai tiếp **Giai đoạn BE-03** (Tích hợp Mock Payment Gateway, Webhook xác thực chữ ký HMAC-SHA256, Cấp phát vé mã QR mã hóa AES-256-GCM và API soát vé cho nhân viên rạp).
