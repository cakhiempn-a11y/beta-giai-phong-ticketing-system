# Báo Cáo Triển Khai Giai Đoạn 5 (`UI-P5`): Giữ Ghế & Thanh Toán Minh Bạch (`/orders/{id}` & `/orders/{id}/checkout`)

- **Dự án:** Hệ thống Đặt vé Rạp chiếu phim Beta Giải Phóng (*MIS Academic Capstone*)
- **Giai đoạn:** `UI-P5` — *Transparent Seat Hold & Academic Mock Checkout (`/orders/{id}` & `/orders/{id}/checkout`)*
- **Tham chiếu kiến trúc:** [`docs/ui/NETFLIX_UI_MASTER_PLAN.md`](NETFLIX_UI_MASTER_PLAN.md) (Mục 11 — Giai đoạn 5 `UI-P5`) & [`docs/ui/prototype/05-checkout-desktop.html`](prototype/05-checkout-desktop.html)
- **Trạng thái:** **HOÀN TẤT & ĐÃ XÁC MINH TOÀN DIỆN (PASS 100%)**

---

## 1. Tóm Tắt Mục Tiêu Đã Đạt Được

Giai đoạn `UI-P5` thiết kế lại và nâng cấp toàn diện hai màn hình giao dịch trọng yếu của khách hàng là **`/orders/{id}`** (`Views/Orders/Details.cshtml`) và **`/orders/{id}/checkout`** (`Views/Orders/Checkout.cshtml`) trên vỏ giao diện điện ảnh `_CinemaLayout.cshtml` với **`ViewData["UseLegacyBootstrap"] = false`** (tách bỏ 100% `bootstrap.min.css`, `bootstrap.bundle.min.js`, `dark-compat.css`, `jQuery`, màu xanh `#0d6efd` và các hộp thoại chặn luồng `window.confirm()` / `window.alert()`):

1. **Đồng Hồ Đếm Ngược Giữ Ghế Chuẩn Máy Chủ & Miễn Nhiễm Lệch Giờ Thiết Bị (`performance.now()`):**
   - Hiển thị dải banner giữ chỗ nổi bật (`[data-testid="order-hold-banner"]`) với thanh tiến trình thời gian thực và đồng hồ `#countdownTimer` (`font-variant-numeric: tabular-nums`) khởi tạo trực tiếp từ `RemainingSeconds` của máy chủ (`HoldExpiresUtc`).
   - Sử dụng đồng hồ đơn điệu `performance.now()` kết hợp sự kiện `visibilitychange`, miễn nhiễm hoàn toàn với sai lệch đồng hồ hệ thống phía client cũng như hiện tượng giảm nhịp khi chuyển tab nền.
   - Khi còn dưới `60` giây, dải banner chuyển sang trạng thái khẩn cấp đỏ kèm thông báo `aria-live="polite"`. Khi chạm mốc **`00:00 (Hết hạn)`**, giao diện tự động khóa toàn bộ nút thanh toán/hủy đơn (`#btnPaySuccess`, `#btnPayFailed`, `#btnProceedCheckout`, `#btnCancel`, `#btnCancelCheckout`), đổi nhãn trạng thái sang `Hết hạn giữ chỗ • 00:00` và mở thẻ hành động nội tuyến `[data-testid="hold-expired-inline-panel"]` dẫn khách quay lại sơ đồ ghế chọn lại ngay.

2. **Chuẩn Hóa 100% Múi Giờ Việt Nam (`UTC+7`) & Hồ Sơ Phim Xác Minh:**
   - Mọi mốc thời gian trên cả hai màn hình (thời điểm tạo đơn, thời hạn khóa ghế, thời điểm xác nhận thanh toán, ngày chiếu và khung giờ `HH:mm → HH:mm` kèm nhãn `+1 ngày` nếu qua nửa đêm) đều quy đổi nhất quán sang múi giờ Việt Nam (`UTC+7`) — tuyệt đối không còn hiển thị chuỗi giờ `UTC` thô gây nhầm lẫn.
   - Khối thông tin tác phẩm hiển thị poster dọc `2:3` đã xác minh (`srcset` `342w`/`780w` kèm fallback trung thực `/images/poster-fallback.svg`), tên tiếng Việt, tên gốc + năm phát hành, đạo diễn, thời lượng, nhãn phân loại độ tuổi tại Việt Nam và điểm đánh giá thật (`IMDb` kèm quy mô lượt bình chọn), cùng bảng đối chiếu ghế (`• Ghế Thường` / `★ Ghế VIP`) và tổng tiền **VND nguyên**.

3. **Xem Trước Cấu Trúc Thẻ Lên Rạp & Mã QR Hậu Thanh Toán (`[data-testid="qr-pass-preview"]`):**
   - Tích hợp thẻ minh họa vé điện tử có rãnh xé cuống vé (`perforated ticket stub`) ngay trên trang Checkout, giải thích rõ nguyên tắc phát hành vé nguyên tử sau khi đạt trạng thái `PAID` với vùng tĩnh trắng tinh `#FFFFFF` (`filter: none`) dành cho máy quét quang học tại cổng soát vé.

4. **Trình Mô Phỏng Thanh Toán Học Thuật MIS Minh Bạch Tuyệt Đối (`[data-testid="mis-payment-simulator"]`):**
   - Ghi nhãn rõ **`MÔI TRƯỜNG MÔ PHỎNG HỌC THUẬT (MIS)`** và **`HMAC-SHA256 Signed`**, công bố minh bạch đây là bộ giả lập callback có chữ ký mật mã phục vụ đề án Hệ thống thông tin quản lý: **không thu tiền thật**, **không kết nối tổ chức tín dụng** và **tuyệt đối không hiển thị bất kỳ ô nhập số thẻ ngân hàng, CVV hay OTP giả nào**.
   - Cung cấp 2 luồng kiểm định rõ ràng: **`Xác nhận Mô phỏng Thành công (SUCCESS)`** và **`Mô phỏng Giao dịch Bị từ chối (FAILED)`**, đồng thời hỗ trợ chế độ ẩn trình mô phỏng an toàn khi `BGP_ENABLE_MOCK_PAYMENT_SIMULATOR=false` (vẫn bảo toàn token chống giả mạo CSRF cho kiểm thử bảo mật `TC_WEB_PAY_23`).

5. **Hộp Thoại `<dialog>` Chuẩn Tiếpận Thay Thế Hoàn Toàn `window.confirm()`:**
   - Mọi thao tác mang tính hủy bỏ hoặc từ chối giao dịch (`#btnCancel`, `#btnCancelCheckout`, `#btnPayFailed`) đều mở hộp thoại HTML5 `<dialog>` gốc (`[data-testid="cancel-order-dialog"]` và `[data-testid="fail-payment-dialog"]`) có lớp nền mờ, quản lý tiêu điểm bàn phím, hỗ trợ đóng bằng phím `Escape` hoặc nhấn ra ngoài vùng nền và tóm tắt rõ số ghế sẽ được giải phóng trước khi gửi biểu mẫu.

6. **Bao Phủ Rõ Ràng Toàn Bộ 6 Trạng Thái Vòng Đời Đơn Hàng Trên `/orders/{id}`:**
   - Thiết kế chuyên biệt về sắc độ, biểu tượng, tiêu đề, thông điệp giải thích nghiệp vụ và nút hành động tiếp theo cho đủ 6 trạng thái: **`PENDING_PAYMENT`** (còn hạn giữ chỗ), **`PAID`** (đã cấp vé QR), **`EXPIRED`** (quá hạn 5 phút), **`CANCELLED`** (khách chủ động hủy), **`FAILED`** (cổng thanh toán từ chối) và **`NEEDS_REVIEW`** (callback thanh toán đến trễ sau khi hết hạn giữ ghế, chờ quản trị viên đối soát hoàn tiền).

---

## 2. Danh Sách Tệp Tạo Mới & Cập Nhật

### Backend, DTO, Services & Controllers
- [`src/BetaGiaiPhong.Web/Models/Responses/BookingDtos.cs`](../../src/BetaGiaiPhong.Web/Models/Responses/BookingDtos.cs): Mở rộng `OrderDetailDto` với các tham số tùy chọn tương thích ngược chứa mốc giờ Việt Nam (`UTC+7`) và hồ sơ phim xác minh (`MovieRating`, `MovieArtwork`, đạo diễn, năm phát hành, phân loại tuổi VN).
- [`src/BetaGiaiPhong.Web/Models/ViewModels/CheckoutViewModel.cs`](../../src/BetaGiaiPhong.Web/Models/ViewModels/CheckoutViewModel.cs), [`OrderDetailViewModel.cs`](../../src/BetaGiaiPhong.Web/Models/ViewModels/OrderDetailViewModel.cs): Bổ sung thông tin suất chiếu `UTC+7`, hồ sơ phim xác minh và thông báo `FlashWarning`.
- [`src/BetaGiaiPhong.Web/Services/BookingQueryService.cs`](../../src/BetaGiaiPhong.Web/Services/BookingQueryService.cs), [`CatalogService.cs`](../../src/BetaGiaiPhong.Web/Services/CatalogService.cs), [`OrdersController.cs`](../../src/BetaGiaiPhong.Web/Controllers/OrdersController.cs): Nạp trọn gói dữ liệu phim xác minh & quy đổi giờ Việt Nam ngay trong truy vấn `AsNoTracking()` duy nhất.
- [`src/BetaGiaiPhong.Runner/Program.cs`](../../src/BetaGiaiPhong.Runner/Program.cs): Bổ sung hai tác vụ `e2e-sql` có rào chắn an toàn chỉ chạy trên cơ sở dữ liệu `BetaGiaiPhong_E2E_*` (`set-order-remaining-seconds` và `set-order-needs-review`) phục vụ kiểm thử đếm ngược thực tế về `00:00` và trạng thái đối soát `NEEDS_REVIEW`.

### Giao Diện CSS, Vanilla ES Module & Razor Views (Zero-Bootstrap)
- [`src/BetaGiaiPhong.Web/wwwroot/css/cinema/page-checkout.css`](../../src/BetaGiaiPhong.Web/wwwroot/css/cinema/page-checkout.css): Hệ thống giao diện tối nhiều lớp cho trang Chi tiết đơn hàng & Thanh toán, dải đồng hồ giữ chỗ, cuống vé xem trước QR nền `#FFFFFF`, bảng điều khiển mô phỏng học thuật MIS, hộp thoại `<dialog>` và bố cục đáp ứng từ `320px` đến `1440px`.
- [`src/BetaGiaiPhong.Web/wwwroot/js/modules/hold-countdown.js`](../../src/BetaGiaiPhong.Web/wwwroot/js/modules/hold-countdown.js): Module ES độc lập vận hành đồng hồ đếm ngược đơn điệu `performance.now()`, tự động khóa nút khi chạm `00:00`, đóng thông báo flash và điều khiển hộp thoại xác nhận `<dialog>`.
- [`src/BetaGiaiPhong.Web/Views/Orders/Details.cshtml`](../../src/BetaGiaiPhong.Web/Views/Orders/Details.cshtml), [`Checkout.cshtml`](../../src/BetaGiaiPhong.Web/Views/Orders/Checkout.cshtml): Nâng cấp sang `_CinemaLayout` với `UseLegacyBootstrap = false`, bảo toàn đầy đủ mọi hợp đồng biểu mẫu POST (`DemoPay`, `Cancel`, `__RequestVerificationToken`) và các bộ chọn kiểm thử hiện hữu (`#countdownTimer`, `#btnPaySuccess`, `#btnPayFailed`, `#btnCancel`).

### Kiểm Thử Tự Động & Công Cụ Chụp Giao Diện
- [`e2e/tests/12-cinematic-checkout-and-orders.spec.ts`](../../e2e/tests/12-cinematic-checkout-and-orders.spec.ts): Bộ kiểm thử Playwright chuyên sâu (`12.1`–`12.4`) kiểm chứng cô lập Zero-Bootstrap, không còn màu `#0d6efd`, poster & điểm IMDb thật, chuẩn hóa `UTC+7`, hộp vùng tĩnh `#FFFFFF` của mẫu vé QR, 0 ô nhập thẻ giả trên trình mô phỏng MIS, xác nhận `<dialog>` với 0 lệnh gọi `window.confirm()`, đếm ngược thực tế chạm `00:00 (Hết hạn)` tự động khóa nút, đủ 6 trạng thái đơn hàng và 0 tràn ngang từ `320px` đến `1440px`.
- [`e2e/tests/02-negative-and-security.spec.ts`](../../e2e/tests/02-negative-and-security.spec.ts), [`04-responsive-and-a11y.spec.ts`](../../e2e/tests/04-responsive-and-a11y.spec.ts), [`11-cinematic-seat-map.spec.ts`](../../e2e/tests/11-cinematic-seat-map.spec.ts): Đồng bộ thao tác xác nhận hủy/thất bại sang hộp thoại `<dialog>` mới.
- [`e2e/tools/capture-ui.ts`](../../e2e/tools/capture-ui.ts): Bổ sung chế độ chụp `ui-p5` xuất trọn bộ 12 ảnh giao diện thực tế vào `docs/ui/screenshots/ui-p5/`.

---

## 3. Bằng Chứng Kiểm Định Kỹ Thuật

| Hạng mục kiểm tra | Lệnh thực thi | Kết quả |
| :--- | :--- | :--- |
| Kiểm tra tĩnh Schema & C# Delimiters | `python scripts/static_schema_checks.py` | **PASS** (`17` bảng, `145` cột, `22` FK, `39` SQL batches) |
| Biên dịch Release toàn giải pháp | `dotnet build -c Release --nologo` | **PASS** (`0 Warning(s)`, `0 Error(s)`) |
| Kiểm tra kiểu tĩnh TypeScript E2E | `npx tsc --noEmit` | **PASS** (`0` lỗi) |
| Kiểm thử đơn vị, tích hợp & bảo mật xUnit | `dotnet test -c Release --no-build --nologo` | **PASS** (`171/171` bài đạt, bao gồm `TC_WEB_PAY_23`) |
| Kiểm thử trình duyệt thật Playwright | `npx playwright test --reporter=list` (`01` đến `12`) | **PASS** (`40/40` bài đạt trên Chromium) |
| Kiểm tra toàn vẹn dữ liệu vận hành | `BetaGiaiPhong.Runner verify` | **PASS** (`0 violation(s)`) |
| Kiểm định tràn ngang đa khung nhìn | `320px`, `390px`, `768px`, `1440px` (kể cả khi mở `<dialog>`) | **PASS** (`scrollWidth <= clientWidth` = `0px` overflow) |

---

## 4. Bộ Ảnh Chụp Giao Diện Thực Tế (`docs/ui/screenshots/ui-p5/`)

Toàn bộ 12 ảnh chụp dưới đây được kết xuất trực tiếp từ trình duyệt Chromium chạy trên Kestrel + SQL Server tạm thời với dữ liệu phim xác minh *Mê trong mê* (*Charade*, 1963 — IMDb `7.8/10`):

1. `01-order-details-active-hold-desktop-1440.png` — Trang `/orders/{id}` ở trạng thái đang giữ ghế (`PENDING_PAYMENT`) với đồng hồ đếm ngược, hồ sơ phim xác minh, giờ `UTC+7` và bảng ghế `A3, C5, C6` (`265.000 đ`).
2. `02-checkout-active-hold-desktop-1440.png` — Trang `/orders/{id}/checkout` Desktop `1440×900` hiển thị cuống vé xem trước mã QR nền `#FFFFFF` và bảng điều khiển mô phỏng học thuật MIS ký số `HMAC-SHA256` (không có ô nhập thẻ giả).
3. `03-checkout-cancel-dialog-desktop-1440.png` — Hộp thoại `<dialog>` xác nhận hủy giữ chỗ và giải phóng ghế trên nền kính mờ, thay thế hoàn toàn `window.confirm()`.
4. `04-checkout-fail-payment-dialog-desktop-1440.png` — Hộp thoại `<dialog>` xác nhận gửi phản hồi từ chối thanh toán (`Outcome = FAILED`) có chữ ký `HMAC-SHA256`.
5. `05-order-details-failed-desktop-1440.png` — Trang `/orders/{id}` ở trạng thái **`FAILED`** (giao dịch bị từ chối, không cấp vé, đã nhả ghế) kèm nút đặt lại ghế mới.
6. `06-checkout-hold-expired-00-00-lockout-1440.png` — Trang `/orders/{id}/checkout` khi đồng hồ chạm mốc **`00:00 (Hết hạn)`**: các nút thanh toán tự động khóa mờ và thẻ chọn lại ghế nội tuyến hiện ra tức thì.
7. `07-order-details-expired-desktop-1440.png` — Trang `/orders/{id}` ở trạng thái **`EXPIRED`** (quá hạn giữ ghế 5 phút) kèm lối dẫn chọn lại ghế cho suất chiếu.
8. `08-order-details-needs-review-desktop-1440.png` — Trang `/orders/{id}` ở trạng thái **`NEEDS_REVIEW`** (phản hồi thanh toán đến trễ sau khi hết hạn giữ chỗ, chờ bộ phận vận hành đối soát).
9. `09-order-details-paid-desktop-1440.png` — Trang `/orders/{id}` ở trạng thái **`PAID`** (đã thanh toán thành công, hoàn tất cả 4 bước tiến trình) kèm nút mở vé điện tử & mã QR.
10. `10-checkout-mobile-390.png` — Toàn trang `/orders/{id}/checkout` trên khung nhìn di động `390×844` với bố cục một cột mạch lạc và cuống vé QR chia rãnh ngang.
11. `11-checkout-narrow-mobile-320.png` — Kiểm chứng khung nhìn di động siêu hẹp `320×568` không tràn ngang (`0px` horizontal overflow).
12. `12-order-details-cancelled-desktop-1440.png` — Trang `/orders/{id}` ở trạng thái **`CANCELLED`** sau khi khách xác nhận hủy giữ chỗ qua hộp thoại `<dialog>`.
