# Báo Cáo Triển Khai Giai Đoạn 6 (`UI-P6`): Vé Điện Tử QR Độ Tương Phản Cao, Lịch Sử Đơn Hàng & Tài Khoản Điện Ảnh

- **Dự án:** Hệ thống Đặt vé Rạp chiếu phim Beta Giải Phóng (*MIS Academic Capstone*)
- **Giai đoạn:** `UI-P6` — *High-Contrast QR Boarding Passes, Order History & Cinema Identity (`/orders/{id}/tickets`, `/orders`, `/account/login`, `/account/profile`)*
- **Tham chiếu kiến trúc:** [`docs/ui/NETFLIX_UI_MASTER_PLAN.md`](NETFLIX_UI_MASTER_PLAN.md) (Mục 11 — Giai đoạn 6 `UI-P6`)
- **Trạng thái:** **HOÀN TẤT & ĐÃ XÁC MINH TOÀN DIỆN (PASS 100%) — 100% MÀN HÌNH KHÁCH HÀNG ĐẠT ZERO-BOOTSTRAP**

---

## 1. Tóm Tắt Mục Tiêu Đã Đạt Được

Giai đoạn `UI-P6` hoàn tất chặng cuối của toàn bộ bề mặt khách hàng (consumer/public routes) bằng việc thiết kế lại bốn tuyến đường **`/orders/{id}/tickets`**, **`/orders`**, **`/account/login`** và **`/account/profile`** trên vỏ giao diện điện ảnh `_CinemaLayout.cshtml` với **`ViewData["UseLegacyBootstrap"] = false`** (tách bỏ hoàn toàn `bootstrap.min.css`, `bootstrap.bundle.min.js`, `dark-compat.css`, `jQuery` và mọi dấu vết màu xanh `#0d6efd` trên 100% trang khách hàng):

1. **Thẻ Lên Rạp Điện Tử Có Rãnh Xé & Vùng Tĩnh Quang Học `#FFFFFF` Bất Biến (`/orders/{id}/tickets`):**
   - Mỗi vé được thiết kế dạng thẻ lên rạp điện ảnh (`perforated cinema boarding pass` `.ticket-card`) có rãnh đục lỗ ngăn cách cuống vé, số ghế cỡ lớn (`A2`, `C5`), nhãn hạng ghế đa tín hiệu (`• Ghế Thường` / `★ Ghế VIP`), mã định danh `TicketCode`, phòng chiếu, mốc giờ chiếu và thời điểm cấp vé chuẩn **`UTC+7`**.
   - **Khóa cứng chuẩn quang học mã QR (`.bgp-qr-box, .bgp-qr-box img`):** Cưỡng bức `background: #FFFFFF !important; padding: 12px !important; filter: none !important; opacity: 1 !important;` với kích thước ảnh `152×152px` (`>= 120×120px` trên cả khung nhìn `320px`). Kiểm thử E2E chụp trực tiếp ảnh điểm ảnh DOM của `.bgp-qr-box` trên nền tối và đưa vào trình quét `Html5Qrcode` tại cổng `/staff/check-in` đạt tỷ lệ giải mã **`HỢP LỆ` 100%**.
   - **Phân tách trực quan tức thì `ISSUED` vs `USED`:** Vé chưa quét nổi bật với viền ngọc lục bảo và huy hiệu `HỢP LỆ / CHƯA SOÁT • ISSUED`; ngay sau khi soát vé qua cổng, vé chuyển sang giao diện trung tính `ticket-card--used` với huy hiệu `ĐÃ SOÁT VÉ (USED)` kèm dấu thời gian **`Thời điểm quét qua cổng ... (UTC+7)`** tra cứu từ nhật ký `CheckinLogs` (`ACCEPTED`), trong khi vẫn giữ vùng tĩnh trắng cho mã QR.
   - **Hồ sơ phim xác minh 100%:** Hiển thị poster dọc `2:3` thật (`srcset` `342w`/`780w` kèm fallback trung thực `/images/poster-fallback.svg`), tên phim tiếng Việt, tên gốc + năm phát hành, đạo diễn, thời lượng, nhãn độ tuổi VN và điểm đánh giá `IMDb` thật kèm số lượt bình chọn.

2. **Chế Độ In Ấn Thân Thiện Mực In (`@media print` trong `page-tickets.css`):**
   - Tự động ẩn thanh điều hướng `.bgp-cinema-header`, chân trang `.bgp-cinema-footer`, thanh đường dẫn, thông báo flash và các nút thao tác (`[data-print-tickets]`, `.bgp-tk-dossier__actions`).
   - Chuyển nền toàn trang và thẻ `.ticket-card` sang trắng tinh `#FFFFFF`, chữ đen sắc nét `#09090B`, nhãn trạng thái `USED`/phòng chiếu tương phản cao `#7F1D1D` trên nền `#FEE2E2` và khóa `break-inside: avoid` để không cắt đôi mã QR giữa hai trang giấy/PDF.

3. **Lịch Sử Đơn Hàng & Vé Điện Tử Tông Tối Điện Ảnh (`/orders`):**
   - Thay thế bảng Bootstrap cũ bằng danh sách thẻ giao dịch nhiều lớp (`[data-testid="order-list-card"]`) hiển thị ảnh poster `2:3` đã xác minh, mã đơn `#BGP-...`, phòng chiếu, giờ chiếu `UTC+7`, thời điểm tạo đơn `UTC+7`, danh sách mã ghế (`A2, C5`), tổng tiền **VND nguyên** và nút hành động ngữ cảnh (`Xem vé QR →` cho đơn `PAID`, `Thanh toán ngay →` cho đơn `PENDING_PAYMENT` còn hạn).
   - Bổ sung thanh lọc trạng thái qua tham số truy vấn GET (`?status=paid`, `?status=pending`, `?status=inactive`), bộ phân trang giữ nguyên bộ lọc và hai trạng thái rỗng chuyên biệt (`Bạn chưa có đơn đặt vé nào` và `Không có đơn hàng nào khớp bộ lọc hiện tại`).

4. **Đăng Nhập Khí Quyển Điện Ảnh (`/account/login`) & Chọn Nhanh Tài Khoản Thử Nghiệm (`Development-Only`):**
   - Thiết kế sân khấu đăng nhập tâm điểm với ánh sáng đỏ subtle phía sau thẻ kính tối, bảo toàn luồng `returnUrl` kèm hộp ghi chú điều hướng rõ ràng (`[data-testid="login-return-url-note"]`), đường viền ô nhập đạt tương phản WCAG AA `4.68:1` (`>= 3.0:1`), thứ tự `Tab` trực tiếp từ `Request.Email` sang `Request.Password`, thông báo lỗi xác thực nổi bật và kiểm tra hợp lệ phía client không phụ thuộc `jQuery` (`.field-validation-error` đúng sắc độ `rgb(248, 113, 113)`).
   - Tích hợp bảng **Chọn nhanh tài khoản thử nghiệm (`[data-testid="dev-demo-account-picker"]`)** chỉ hiển thị khi `IWebHostEnvironment.IsDevelopment() == true`: nhấn vào chip vai trò (`customer1`, `customer2`, `staff`, `manager`) tự động điền địa chỉ email và chuyển tiêu điểm vào ô mật khẩu — **tuyệt đối không nhúng mật khẩu trong mã HTML/JS**.

5. **Hồ Sơ Định Danh & Phân Quyền Hệ Thống (`/account/profile`):**
   - Hiển thị thẻ định danh người dùng với avatar chữ cái đầu, trạng thái `ACTIVE`, email xác thực, thời điểm khởi tạo tài khoản theo **`UTC+7`**, thống kê giao dịch (`đơn PAID / tổng đơn`), huy hiệu vai trò RBAC (`CUSTOMER`, `STAFF`, `MANAGER`, `ADMIN`), lối tắt thao tác theo đúng quyền hạn và biểu mẫu POST đăng xuất có chống giả mạo CSRF (`@Html.AntiForgeryToken()`).

---

## 2. Danh Sách Tệp Tạo Mới & Cập Nhật

### Backend, DTO, ViewModels, Services & Controllers
- [`src/BetaGiaiPhong.Web/Models/Responses/TicketDtos.cs`](../../src/BetaGiaiPhong.Web/Models/Responses/TicketDtos.cs), [`OrderTicketsViewModel.cs`](../../src/BetaGiaiPhong.Web/Models/ViewModels/OrderTicketsViewModel.cs), [`TicketQueryService.cs`](../../src/BetaGiaiPhong.Web/Services/TicketQueryService.cs): Bổ sung mốc giờ cấp vé & thời điểm quét cổng `UTC+7` (`IssuedVietnamText`, `UsedVietnamText`) cùng hồ sơ phim xác minh trọn gói cho màn hình vé điện tử.
- [`src/BetaGiaiPhong.Web/Models/Responses/BookingDtos.cs`](../../src/BetaGiaiPhong.Web/Models/Responses/BookingDtos.cs), [`BookingQueryService.cs`](../../src/BetaGiaiPhong.Web/Services/BookingQueryService.cs), [`OrdersController.cs`](../../src/BetaGiaiPhong.Web/Controllers/OrdersController.cs): Hỗ trợ lọc trạng thái `?status=paid|pending|inactive`, nạp poster xác minh, tên phòng, tóm tắt mã ghế và giờ tạo đơn `UTC+7` cho `/orders`, đồng thời truyền `FlashMessage` sang `/orders/{id}/tickets`.
- [`src/BetaGiaiPhong.Web/Models/Responses/UserDtos.cs`](../../src/BetaGiaiPhong.Web/Models/Responses/UserDtos.cs), [`AuthService.cs`](../../src/BetaGiaiPhong.Web/Services/AuthService.cs): Bổ sung `CreatedVietnamText`, `TotalOrdersCount`, `PaidOrdersCount` trên `UserProfileDto`.
- [`src/BetaGiaiPhong.Web/Models/ViewModels/LoginViewModel.cs`](../../src/BetaGiaiPhong.Web/Models/ViewModels/LoginViewModel.cs), [`AccountController.cs`](../../src/BetaGiaiPhong.Web/Controllers/AccountController.cs): Gắn cờ `IsDevelopment` từ `IWebHostEnvironment` để kiểm soát an toàn khung chọn nhanh tài khoản Demo Seed.
- [`src/BetaGiaiPhong.Runner/Program.cs`](../../src/BetaGiaiPhong.Runner/Program.cs): Bổ sung lệnh `e2e-sql seed-empty-customer` có khóa bảo vệ chỉ chạy trên cơ sở dữ liệu `BetaGiaiPhong_E2E_*` để kiểm định trạng thái rỗng 0 đơn hàng độc lập.

### Giao Diện CSS, Vanilla ES Modules & Razor Views (Zero-Bootstrap)
- [`src/BetaGiaiPhong.Web/wwwroot/css/cinema/page-tickets.css`](../../src/BetaGiaiPhong.Web/wwwroot/css/cinema/page-tickets.css): Thiết kế hệ thống thẻ lên rạp đục lỗ `.ticket-card`, vùng tĩnh quang học `.bgp-qr-box` nền `#FFFFFF !important`, bố cục `@media print` đen-trên-trắng và danh sách lịch sử đơn hàng `/orders`.
- [`src/BetaGiaiPhong.Web/wwwroot/css/cinema/page-account.css`](../../src/BetaGiaiPhong.Web/wwwroot/css/cinema/page-account.css): Thiết kế trang đăng nhập `/account/login`, khung chọn nhanh Demo Seed và trang hồ sơ `/account/profile`.
- [`src/BetaGiaiPhong.Web/wwwroot/js/modules/tickets-print.js`](../../src/BetaGiaiPhong.Web/wwwroot/js/modules/tickets-print.js), [`account-login.js`](../../src/BetaGiaiPhong.Web/wwwroot/js/modules/account-login.js): Module ES thuần xử lý nút in vé (`window.print()`), đóng thông báo flash, kiểm tra hợp lệ biểu mẫu đăng nhập không cần `jQuery` và điền nhanh email thử nghiệm.
- [`src/BetaGiaiPhong.Web/Views/Orders/Tickets.cshtml`](../../src/BetaGiaiPhong.Web/Views/Orders/Tickets.cshtml), [`Views/Orders/Index.cshtml`](../../src/BetaGiaiPhong.Web/Views/Orders/Index.cshtml), [`Views/Account/Login.cshtml`](../../src/BetaGiaiPhong.Web/Views/Account/Login.cshtml), [`Views/Account/Profile.cshtml`](../../src/BetaGiaiPhong.Web/Views/Account/Profile.cshtml): Chuyển đổi hoàn toàn sang `_CinemaLayout` với `ViewData["UseLegacyBootstrap"] = false`.

### Kiểm Thử Tự Động & Công Cụ Chụp Giao Diện
- [`e2e/tests/13-cinematic-tickets-orders-account.spec.ts`](../../e2e/tests/13-cinematic-tickets-orders-account.spec.ts): Bộ kiểm thử Playwright chuyên sâu (`13.1`–`13.4`) xác minh cô lập Zero-Bootstrap, vùng tĩnh QR `#FFFFFF` (`padding >= 12px`, `filter: none`, `>= 120×120px`), quét quang học ảnh chụp DOM thực tế từ `ISSUED` sang `USED`, chế độ in `@media print`, bộ lọc & trạng thái rỗng `/orders`, luồng đăng nhập `returnUrl` + Demo Picker + đăng xuất CSRF, không rò rỉ token/mật khẩu và `0px` tràn ngang trên `320px`, `390px`, `768px`, `1440px`.
- [`e2e/tools/capture-ui.ts`](../../e2e/tools/capture-ui.ts): Bổ sung chế độ chụp `ui-p6` xuất trọn bộ 10 ảnh bằng chứng giao diện thực tế vào `docs/ui/screenshots/ui-p6/`.

---

## 3. Bằng Chứng Kiểm Định Kỹ Thuật

| Hạng mục kiểm tra | Lệnh thực thi | Kết quả |
| :--- | :--- | :--- |
| Kiểm tra tĩnh Schema & C# Delimiters | `python scripts/static_schema_checks.py` | **PASS** (`17` bảng, `145` cột, `22` FK, `39` SQL batches) |
| Biên dịch Release toàn giải pháp | `dotnet build -c Release --nologo` | **PASS** (`0 Warning(s)`, `0 Error(s)`) |
| Kiểm tra kiểu tĩnh TypeScript E2E | `npx tsc --noEmit` | **PASS** (`0` lỗi) |
| Kiểm thử đơn vị, tích hợp & bảo mật xUnit | `dotnet test -c Release --no-build --nologo` | **PASS** (`171/171` bài đạt) |
| Kiểm thử trình duyệt thật Playwright | `npx playwright test --reporter=list` (`01` đến `13`) | **PASS** (`44/44` bài đạt trên Chromium) |
| Quét quang học QR từ ảnh DOM kết xuất thật | `13.1`: `.bgp-qr-box.screenshot()` → `/staff/check-in` `#qrFileInput` | **PASS** (`HỢP LỆ` → chuyển trạng thái `ĐÃ SOÁT VÉ (USED)` kèm giờ `UTC+7`) |
| Kiểm tra toàn vẹn dữ liệu vận hành | `BetaGiaiPhong.Runner verify` | **PASS** (`0 violation(s)`) |
| Kiểm định tràn ngang & kích thước QR đa khung nhìn | `320px`, `390px`, `768px`, `1440px` trên cả 4 route `UI-P6` | **PASS** (`0px` overflow; ảnh QR luôn `>= 152×152px`) |

---

## 4. Bộ Ảnh Chụp Giao Diện Thực Tế (`docs/ui/screenshots/ui-p6/`)

Toàn bộ 10 ảnh chụp dưới đây được kết xuất từ trình duyệt Chromium chạy trên Kestrel + SQL Server tạm thời với dữ liệu tác phẩm xác minh *Mê trong mê* (*Charade*, 1963 — IMDb `7.8/10`) và *His Girl Friday* (1940):

1. `01-tickets-qr-issued-and-used-desktop-1440.png` — Trang `/orders/{id}/tickets` Desktop `1440×900` đặt cạnh nhau 1 vé **`ĐÃ SOÁT VÉ (USED)`** (ghế `A2`, có dấu thời gian quét qua cổng `UTC+7`) và 1 vé **`HỢP LỆ / CHƯA SOÁT • ISSUED`** (ghế VIP `C5`), cùng vùng tĩnh QR `#FFFFFF` tinh khiết.
2. `02-tickets-qr-mobile-390.png` — Trang `/orders/{id}/tickets` trên khung nhìn di động `390×844` với cuống vé đục lỗ chia ngang và mã QR giữ nguyên kích thước chuẩn quang học.
3. `03-tickets-qr-print-mode-1440.png` — Trang `/orders/{id}/tickets` trong chế độ in ấn `@media print`: nền trắng tiết kiệm mực, chữ đen sắc nét, tự động ẩn header/footer/nút bấm.
4. `04-orders-history-populated-desktop-1440.png` — Trang `/orders` Desktop `1440×900` hiển thị đồng thời 3 trạng thái đơn (`CHỜ THANH TOÁN`, `ĐÃ THANH TOÁN • PAID`, `ĐÃ HỦY`) với poster `2:3` xác minh và mốc giờ `UTC+7`.
5. `05-orders-history-filtered-paid-desktop-1440.png` — Trang `/orders?status=paid` khi lọc riêng các đơn hàng đã phát hành vé điện tử QR.
6. `06-orders-history-mobile-390.png` — Trang `/orders` trên khung nhìn di động `390×844` không tràn ngang, thao tác nút chạm rõ ràng.
7. `07-orders-history-empty-desktop-1440.png` — Trạng thái rỗng (`Bạn chưa có đơn đặt vé nào`) trên `/orders` dành cho tài khoản khách hàng mới.
8. `08-account-login-desktop-1440.png` — Trang `/account/login?returnUrl=%2Forders` Desktop `1440×900` kèm thông báo bảo toàn `returnUrl` và bảng chọn nhanh tài khoản Demo Seed (`Development Only`, không lộ mật khẩu).
9. `09-account-login-error-mobile-390.png` — Trang `/account/login` trên khung nhìn di động `390×844` hiển thị cảnh báo xác thực thất bại rõ ràng mà vẫn giữ nguyên `returnUrl`.
10. `10-account-profile-customer-desktop-1440.png` — Trang `/account/profile` Desktop `1440×900` hiển thị định danh `CUSTOMER`, giờ khởi tạo `UTC+7`, thống kê `1 đơn PAID / 3 đơn`, lối tắt ngữ cảnh và nút đăng xuất an toàn.
